package com.musichub.mobile.data.sync

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.musichub.mobile.MusicHubApp
import com.musichub.mobile.data.api.ApiCallException
import com.musichub.mobile.data.api.runApi
import com.musichub.mobile.data.api.models.PlaybackRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Conflict-safe outbox drain.
 *
 * - Favorites are last-write-wins: the newest desired state for a track is sent, and an item is
 *   deleted only if it was not superseded while the request was in flight.
 * - A 404 (track deleted on the server) is treated as authoritative and reverts the local flag.
 * - Playback events are append-only on the server; they are retried with a bounded attempt count.
 */
class SyncWorker(
    context: Context,
    params: WorkerParameters,
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        val container = (applicationContext as MusicHubApp).container
        if (!container.sessionManager.ensureAuthenticated()) return@withContext Result.success()
        val profile = container.sessionManager.currentProfile() ?: return@withContext Result.success()
        val api = container.apiFactory.authenticatedApi(profile.baseUrl)
        val syncDao = container.database.sync()
        val trackDao = container.database.tracks()
        var transientFailure = false

        val favorites = syncDao.pendingFavorites()
        for (item in favorites) {
            try {
                runApi {
                    if (item.desired) api.addFavorite(item.trackId) else api.removeFavorite(item.trackId)
                }
                val current = syncDao.pendingFavorites().firstOrNull { it.trackId == item.trackId }
                if (current?.updatedAt == item.updatedAt) {
                    syncDao.deletePendingFavorite(item.trackId)
                }
            } catch (exception: ApiCallException) {
                when (exception.statusCode) {
                    404 -> {
                        trackDao.setFavorite(item.trackId, !item.desired)
                        syncDao.deletePendingFavorite(item.trackId)
                    }

                    401 -> {
                        transientFailure = true
                        return@withContext Result.retry()
                    }

                    else -> transientFailure = true
                }
            }
        }

        val playback = syncDao.pendingPlayback()
        for (item in playback) {
            if (item.attempts >= MAX_PLAYBACK_ATTEMPTS) {
                syncDao.deletePendingPlayback(item.id)
                continue
            }
            try {
                runApi {
                    api.reportPlayback(
                        PlaybackRequest(
                            trackId = item.trackId,
                            positionSeconds = item.positionSeconds,
                            completed = item.completed,
                            deviceId = item.deviceId,
                        ),
                    )
                }
                syncDao.deletePendingPlayback(item.id)
            } catch (exception: ApiCallException) {
                if (exception.statusCode == 404) {
                    syncDao.deletePendingPlayback(item.id)
                } else {
                    syncDao.markPlaybackAttempt(item.id, exception.serverMessage ?: "sync failed")
                    transientFailure = true
                }
            }
        }

        if (transientFailure && runAttemptCount < MAX_WORK_ATTEMPTS) Result.retry() else Result.success()
    }

    private companion object {
        const val MAX_PLAYBACK_ATTEMPTS = 5
        const val MAX_WORK_ATTEMPTS = 6
    }
}
