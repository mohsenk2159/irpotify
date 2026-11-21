package com.musichub.mobile.data.download

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.StatFs
import androidx.core.app.NotificationCompat
import androidx.work.CoroutineWorker
import androidx.work.Data
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import com.musichub.mobile.MainActivity
import com.musichub.mobile.MusicHubApp
import com.musichub.mobile.core.DownloadStateMachine
import com.musichub.mobile.core.DownloadStatus
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Request
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

/**
 * Resumable, integrity-checked offline download.
 *
 * Uses the authenticated stream endpoint with HTTP Range. Partial data lives in a `.part` file and
 * is only promoted after the server-provided SHA-256 (and size, when known) matches.
 */
class DownloadWorker(
    context: Context,
    params: WorkerParameters,
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        val profileId = inputData.getString(KEY_PROFILE_ID) ?: return@withContext Result.failure()
        val trackId = inputData.getString(KEY_TRACK_ID) ?: return@withContext Result.failure()
        val container = (applicationContext as MusicHubApp).container
        val download = container.database.downloads().get(profileId, trackId)
            ?: return@withContext Result.success()
        if (download.status == DownloadStatus.COMPLETED &&
            download.filePath?.let { File(it).exists() } == true
        ) {
            return@withContext Result.success()
        }

        val profile = container.sessionManager.currentProfile()
        if (profile == null || profile.id != profileId) {
            return@withContext Result.failure()
        }
        if (!container.sessionManager.ensureAuthenticated()) {
            return@withContext if (runAttemptCount < MAX_ATTEMPTS) Result.retry() else Result.failure()
        }

        val extension = download.extension
        val finalFile = container.downloadRepository.downloadTargetFile(profileId, trackId, extension)
        val partFile = container.downloadRepository.partFile(profileId, trackId, extension)
        partFile.parentFile?.mkdirs()

        val available = StatFs(partFile.parentFile?.absolutePath ?: applicationContext.filesDir.path).availableBytes
        val remainingEstimate = (download.expectedBytes - partFile.length()).coerceAtLeast(0)
        if (remainingEstimate > 0 && available < remainingEstimate + MIN_FREE_BYTES) {
            container.database.downloads().update(
                profileId = profileId,
                trackId = trackId,
                status = DownloadStatus.FAILED,
                bytes = partFile.length(),
                error = "Not enough free storage for this download",
                filePath = null,
                updatedAt = System.currentTimeMillis(),
            )
            return@withContext Result.failure()
        }

        val currentStatus = download.status
        if (currentStatus == DownloadStatus.QUEUED || currentStatus == DownloadStatus.PAUSED) {
            container.database.downloads().update(
                profileId = profileId,
                trackId = trackId,
                status = DownloadStatus.RUNNING,
                bytes = partFile.length(),
                error = null,
                filePath = null,
                updatedAt = System.currentTimeMillis(),
            )
        }

        try {
            setForeground(foregroundInfo(download.title, partFile.length(), download.expectedBytes))

            val offset = if (partFile.exists()) partFile.length() else 0L
            val requestBuilder = Request.Builder()
                .url("${profile.baseUrl.trimEnd('/')}/api/tracks/$trackId/stream")
                .get()
            if (offset > 0) requestBuilder.header("Range", "bytes=$offset-")

            val response = container.apiFactory.authenticatedClient.newCall(requestBuilder.build()).execute()
            response.use {
                if (!response.isSuccessful) {
                    if (response.code == 401 || response.code == 403) {
                        return@withContext retryOrFail(container, profileId, trackId, "Not authorized to download this track")
                    }
                    if (response.code == 404) {
                        markFailed(container, profileId, trackId, "Track is no longer available", partFile.length())
                        return@withContext Result.failure()
                    }
                    return@withContext retryOrFail(container, profileId, trackId, "Server returned HTTP ${response.code}")
                }

                val body = response.body ?: return@withContext retryOrFail(
                    container, profileId, trackId, "Empty response body",
                )

                var append = offset > 0 && response.code == 206
                if (offset > 0 && response.code == 200) {
                    partFile.delete()
                    append = false
                }

                val expectedTotal = body.contentLength().takeIf { it >= 0 }?.let { length ->
                    if (append) offset + length else length
                } ?: download.expectedBytes

                val output = FileOutputStream(partFile, append)
                val buffer = ByteArray(64 * 1024)
                var written = if (append) offset else 0L
                var lastPublished = written
                var lastNotification = 0L
                val startedAt = System.currentTimeMillis()
                try {
                    body.byteStream().use { input ->
                        while (true) {
                            if (isStopped) {
                                container.database.downloads().update(
                                    profileId = profileId,
                                    trackId = trackId,
                                    status = DownloadStatus.PAUSED,
                                    bytes = written,
                                    error = null,
                                    filePath = null,
                                    updatedAt = System.currentTimeMillis(),
                                )
                                return@withContext Result.retry()
                            }
                            val read = input.read(buffer)
                            if (read <= 0) break
                            output.write(buffer, 0, read)
                            written += read
                            if (written - lastPublished >= PROGRESS_STEP_BYTES) {
                                lastPublished = written
                                setProgress(progressData(written, expectedTotal))
                                val now = System.currentTimeMillis()
                                if (now - lastNotification >= NOTIFICATION_INTERVAL_MS) {
                                    lastNotification = now
                                    setForeground(foregroundInfo(download.title, written, expectedTotal))
                                }
                            }
                        }
                    }
                    output.flush()
                    output.fd.sync()
                } finally {
                    output.close()
                }

                // If the server closed early, keep the partial file for a Range resume.
                if (expectedTotal > 0 && written < expectedTotal) {
                    container.database.downloads().update(
                        profileId = profileId,
                        trackId = trackId,
                        status = DownloadStatus.PAUSED,
                        bytes = written,
                        error = "Transfer interrupted at $written of $expectedTotal bytes",
                        filePath = null,
                        updatedAt = System.currentTimeMillis(),
                    )
                    return@withContext Result.retry()
                }

                container.database.downloads().update(
                    profileId = profileId,
                    trackId = trackId,
                    status = DownloadStatus.VERIFYING,
                    bytes = written,
                    error = null,
                    filePath = null,
                    updatedAt = System.currentTimeMillis(),
                )
                setForeground(foregroundInfo(download.title, written, written, verifying = true))

                val expectedSize = download.expectedBytes
                if (expectedSize > 0 && partFile.length() != expectedSize) {
                    partFile.delete()
                    markFailed(container, profileId, trackId, "Size mismatch after download", 0)
                    return@withContext Result.failure()
                }

                if (download.sha256.isNotBlank()) {
                    val actual = sha256(partFile)
                    if (!actual.equals(download.sha256, ignoreCase = true)) {
                        partFile.delete()
                        markFailed(container, profileId, trackId, "Integrity check failed (SHA-256 mismatch)", 0)
                        return@withContext Result.failure()
                    }
                }

                if (finalFile.exists()) finalFile.delete()
                if (!partFile.renameTo(finalFile)) {
                    partFile.copyTo(finalFile, overwrite = true)
                    partFile.delete()
                }

                container.database.downloads().update(
                    profileId = profileId,
                    trackId = trackId,
                    status = DownloadStatus.COMPLETED,
                    bytes = finalFile.length(),
                    error = null,
                    filePath = finalFile.absolutePath,
                    updatedAt = System.currentTimeMillis(),
                )
                setProgress(workDataOf(KEY_BYTES to finalFile.length(), KEY_TOTAL to finalFile.length()))
                Result.success()
            }
        } catch (exception: IOException) {
            container.database.downloads().update(
                profileId = profileId,
                trackId = trackId,
                status = if (runAttemptCount + 1 >= MAX_ATTEMPTS) DownloadStatus.FAILED else DownloadStatus.PAUSED,
                bytes = if (partFile.exists()) partFile.length() else 0,
                error = exception.message ?: "Download interrupted",
                filePath = null,
                updatedAt = System.currentTimeMillis(),
            )
            if (runAttemptCount + 1 < MAX_ATTEMPTS) Result.retry() else Result.failure()
        } catch (exception: SecurityException) {
            markFailed(container, profileId, trackId, "Storage permission error", partFile.length())
            Result.failure()
        }
    }

    private suspend fun retryOrFail(
        container: com.musichub.mobile.data.AppContainer,
        profileId: String,
        trackId: String,
        message: String,
    ): Result {
        val attempts = runAttemptCount + 1
        container.database.downloads().update(
            profileId = profileId,
            trackId = trackId,
            status = if (attempts >= MAX_ATTEMPTS) DownloadStatus.FAILED else DownloadStatus.PAUSED,
            bytes = 0,
            error = message,
            filePath = null,
            updatedAt = System.currentTimeMillis(),
        )
        return if (attempts < MAX_ATTEMPTS) Result.retry() else Result.failure()
    }

    private suspend fun markFailed(
        container: com.musichub.mobile.data.AppContainer,
        profileId: String,
        trackId: String,
        message: String,
        bytes: Long,
    ) {
        container.database.downloads().update(
            profileId = profileId,
            trackId = trackId,
            status = DownloadStatus.FAILED,
            bytes = bytes,
            error = message,
            filePath = null,
            updatedAt = System.currentTimeMillis(),
        )
    }

    private fun progressData(bytes: Long, total: Long): Data =
        workDataOf(KEY_BYTES to bytes, KEY_TOTAL to total)

    private fun foregroundInfo(
        title: String,
        bytes: Long,
        total: Long,
        verifying: Boolean = false,
    ): ForegroundInfo {
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        if (manager.getNotificationChannel(CHANNEL_ID) == null) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL_ID, "Downloads", NotificationManager.IMPORTANCE_LOW).apply {
                    setShowBadge(false)
                },
            )
        }
        val intent = PendingIntent.getActivity(
            applicationContext,
            0,
            Intent(applicationContext, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val max = total.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        val progress = bytes.coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        val notification = NotificationCompat.Builder(applicationContext, CHANNEL_ID)
            .setContentTitle(title)
            .setContentText(if (verifying) "Verifying download…" else "Downloading for offline playback")
            .setSmallIcon(android.R.drawable.stat_sys_download)
            .setProgress(max, progress, max <= 0)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setContentIntent(intent)
            .build()
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ForegroundInfo(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            ForegroundInfo(NOTIFICATION_ID, notification)
        }
    }

    companion object {
        const val KEY_PROFILE_ID = "profileId"
        const val KEY_TRACK_ID = "trackId"
        const val KEY_BYTES = "bytes"
        const val KEY_TOTAL = "total"
        private const val CHANNEL_ID = "musichub_downloads"
        private const val NOTIFICATION_ID = 4211
        private const val MAX_ATTEMPTS = 5
        private const val MIN_FREE_BYTES = 32L * 1024 * 1024
        private const val PROGRESS_STEP_BYTES = 512L * 1024
        private const val NOTIFICATION_INTERVAL_MS = 1500L

        fun sha256(file: File): String {
            val digest = MessageDigest.getInstance("SHA-256")
            file.inputStream().use { input ->
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val read = input.read(buffer)
                    if (read <= 0) break
                    digest.update(buffer, 0, read)
                }
            }
            return digest.digest().joinToString("") { "%02x".format(it) }
        }
    }
}
