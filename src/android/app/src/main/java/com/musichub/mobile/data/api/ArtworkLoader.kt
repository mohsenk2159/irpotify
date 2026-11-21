package com.musichub.mobile.data.api

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * Small authenticated artwork loader. Uses the shared OkHttp client (bearer header added by the
 * interceptor) and a memory cache. Artwork URLs are server-relative (for example
 * `/api/tracks/{id}/artwork`).
 */
class ArtworkLoader(
    private val client: OkHttpClient,
    private val baseUrlProvider: () -> String?,
) {
    private val cache = LruCache<String, Bitmap>(48)

    fun resolve(path: String?): String? {
        if (path.isNullOrBlank()) return null
        if (path.startsWith("http://") || path.startsWith("https://")) return path
        val base = baseUrlProvider() ?: return null
        return base.trimEnd('/') + "/" + path.trimStart('/')
    }

    suspend fun load(path: String?): Bitmap? = withContext(Dispatchers.IO) {
        val url = resolve(path) ?: return@withContext null
        cache.get(url)?.let { return@withContext it }
        val request = Request.Builder().url(url).get().build()
        runCatching {
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@use null
                val bytes = response.body?.bytes() ?: return@use null
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
                var sample = 1
                while (bounds.outWidth / sample > 1024 || bounds.outHeight / sample > 1024) {
                    sample *= 2
                }
                val options = BitmapFactory.Options().apply { inSampleSize = sample }
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)
            }
        }.getOrNull()?.also { cache.put(url, it) }
    }
}
