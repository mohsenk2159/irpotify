package com.musichub.mobile.playback

import android.content.Context
import android.net.Uri
import androidx.media3.common.C
import androidx.media3.datasource.BaseDataSource
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.okhttp.OkHttpDataSource
import okhttp3.OkHttpClient
import java.io.IOException

/**
 * Resolves the app's private `musichub://track/{id}` media URIs to the real stream endpoint.
 * Downloaded tracks use a plain file:// URI and never touch this data source.
 *
 * Authorization is supplied by the shared OkHttp client's auth interceptor; stream URLs in logs
 * or UI state therefore never contain bearer or stream tokens.
 */
class MusicHubDataSource(
    private val client: OkHttpClient,
    private val baseUrlProvider: () -> String?,
) : BaseDataSource(false) {

    private var upstream: DataSource? = null

    override fun open(dataSpec: DataSpec): Long {
        val trackId = dataSpec.uri.lastPathSegment
            ?: throw IOException("Invalid MusicHub media URI")
        val baseUrl = baseUrlProvider() ?: throw IOException("No active MusicHub session")
        val streamUri = Uri.parse(baseUrl.trimEnd('/') + "/api/tracks/" + trackId + "/stream")
        transferInitializing(dataSpec)
        val httpDataSource = OkHttpDataSource.Factory(client).createDataSource()
        upstream = httpDataSource
        val resolved = dataSpec.buildUpon().setUri(streamUri).build()
        val length = httpDataSource.open(resolved)
        transferStarted(resolved)
        return length
    }

    override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
        val source = upstream ?: return C.RESULT_END_OF_INPUT
        val read = source.read(buffer, offset, length)
        if (read > 0) bytesTransferred(read)
        return read
    }

    override fun getUri(): Uri? = upstream?.uri

    override fun close() {
        try {
            upstream?.close()
        } finally {
            upstream = null
            transferEnded()
        }
    }

    class Factory(
        private val client: OkHttpClient,
        private val baseUrlProvider: () -> String?,
    ) : DataSource.Factory {
        override fun createDataSource(): DataSource = MusicHubDataSource(client, baseUrlProvider)
    }
}
