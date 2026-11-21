package com.musichub.mobile.data.download

import android.content.Context
import androidx.work.Constraints
import androidx.work.Data
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import com.musichub.mobile.core.DownloadStatus
import com.musichub.mobile.data.api.ApiFactory
import com.musichub.mobile.data.auth.SessionManager
import com.musichub.mobile.data.local.DownloadEntity
import com.musichub.mobile.data.local.MusicHubDatabase
import com.musichub.mobile.data.local.TrackEntity
import com.musichub.mobile.data.settings.SettingsStore
import kotlinx.coroutines.flow.Flow
import java.io.File

class DownloadRepository(
    private val context: Context,
    private val database: MusicHubDatabase,
    private val sessionManager: SessionManager,
    private val settingsStore: SettingsStore,
    private val apiFactory: ApiFactory,
) {
    fun observeDownloads(profileId: String): Flow<List<DownloadEntity>> =
        database.downloads().observeDownloads(profileId)

    fun observeDownload(profileId: String, trackId: String): Flow<DownloadEntity?> =
        database.downloads().observe(profileId, trackId)

    suspend fun download(profileId: String, trackId: String): DownloadEntity? =
        database.downloads().get(profileId, trackId)

    suspend fun enqueue(track: TrackEntity): Result<Unit> {
        val profile = sessionManager.currentProfile()
            ?: return Result.failure(IllegalStateException("Sign in before downloading"))
        val existing = database.downloads().get(profile.id, track.id)
        if (existing?.status == DownloadStatus.COMPLETED && existing.filePath?.let { File(it).exists() } == true) {
            return Result.success(Unit)
        }

        val extension = extensionFor(track.codec)
        val directory = profileDirectory(profile.id)
        val target = File(directory, "${track.id}.$extension")
        val part = File(directory, "${track.id}.$extension.part")

        val entity = DownloadEntity(
            profileId = profile.id,
            trackId = track.id,
            title = track.title,
            artist = track.artist,
            album = track.album,
            mimeType = null,
            extension = extension,
            expectedBytes = track.sizeBytes,
            sha256 = track.sha256,
            filePath = null,
            status = DownloadStatus.QUEUED,
            bytesDownloaded = part.takeIf { it.exists() }?.length() ?: 0,
            error = null,
            createdAt = existing?.createdAt ?: System.currentTimeMillis(),
            updatedAt = System.currentTimeMillis(),
        )
        database.downloads().upsert(entity)
        downloadTargetFile(profile.id, track.id, extension).let { if (it.exists()) it.delete() }
        schedule(profile.id, track.id, replace = false)
        return Result.success(Unit)
    }

    suspend fun retry(profileId: String, trackId: String): Result<Unit> {
        val existing = database.downloads().get(profileId, trackId)
            ?: return Result.failure(IllegalStateException("No download for this track"))
        if (!com.musichub.mobile.core.DownloadStateMachine.canRetry(existing.status)) {
            return Result.failure(IllegalStateException("Download cannot be retried from ${existing.status}"))
        }
        database.downloads().update(
            profileId = profileId,
            trackId = trackId,
            status = DownloadStatus.QUEUED,
            bytes = existing.bytesDownloaded,
            error = null,
            filePath = null,
            updatedAt = System.currentTimeMillis(),
        )
        schedule(profileId, trackId, replace = true)
        return Result.success(Unit)
    }

    suspend fun delete(profileId: String, trackId: String) {
        val existing = database.downloads().get(profileId, trackId)
        WorkManager.getInstance(context).cancelUniqueWork(workName(profileId, trackId))
        downloadTargetFile(profileId, trackId, existing?.extension ?: "bin").delete()
        partFile(profileId, trackId, existing?.extension ?: "bin").delete()
        database.downloads().delete(profileId, trackId)
    }

    suspend fun localFile(profileId: String, trackId: String): File? {
        val download = database.downloads().get(profileId, trackId) ?: return null
        if (download.status != DownloadStatus.COMPLETED) return null
        val path = download.filePath ?: return null
        val file = File(path)
        return file.takeIf { it.exists() && it.length() > 0 }
    }

    fun profileDirectory(profileId: String): File =
        File(context.filesDir, "downloads/$profileId").also { it.mkdirs() }

    fun downloadTargetFile(profileId: String, trackId: String, extension: String): File =
        File(profileDirectory(profileId), "$trackId.$extension")

    fun partFile(profileId: String, trackId: String, extension: String): File =
        File(profileDirectory(profileId), "$trackId.$extension.part")

    private fun schedule(profileId: String, trackId: String, replace: Boolean) {
        val wifiOnly = settingsStore.settings.value.downloadWifiOnly
        val constraints = Constraints.Builder()
            .setRequiredNetworkType(if (wifiOnly) NetworkType.UNMETERED else NetworkType.CONNECTED)
            .build()
        val request = OneTimeWorkRequestBuilder<DownloadWorker>()
            .setConstraints(constraints)
            .setInputData(
                Data.Builder()
                    .putString(DownloadWorker.KEY_PROFILE_ID, profileId)
                    .putString(DownloadWorker.KEY_TRACK_ID, trackId)
                    .build(),
            )
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(
            workName(profileId, trackId),
            if (replace) ExistingWorkPolicy.REPLACE else ExistingWorkPolicy.KEEP,
            request,
        )
    }

    private fun workName(profileId: String, trackId: String) = "download-$profileId-$trackId"

    companion object {
        fun extensionFor(codec: String?): String {
            val candidate = codec.orEmpty().trim().lowercase()
            val normalized = when {
                candidate.contains("mpeg") || candidate == "mp3" -> "mp3"
                candidate.contains("flac") -> "flac"
                candidate.contains("aac") || candidate.contains("mp4") || candidate.contains("m4a") -> "m4a"
                candidate.contains("vorbis") || candidate.contains("ogg") -> "ogg"
                candidate.contains("opus") -> "opus"
                candidate.contains("wav") || candidate.contains("pcm") -> "wav"
                else -> "bin"
            }
            return normalized
        }
    }
}
