package com.musichub.mobile.data.api

import com.musichub.mobile.data.api.models.AlbumDetailResponse
import com.musichub.mobile.data.api.models.AlbumDto
import com.musichub.mobile.data.api.models.ArtistDetailResponse
import com.musichub.mobile.data.api.models.ArtistDto
import com.musichub.mobile.data.api.models.FolderNodeDto
import com.musichub.mobile.data.api.models.GenreDto
import com.musichub.mobile.data.api.models.HistoryEntryDto
import com.musichub.mobile.data.api.models.HomeResponse
import com.musichub.mobile.data.api.models.ChangePasswordRequest
import com.musichub.mobile.data.api.models.LoginRequest
import com.musichub.mobile.data.api.models.PagedResult
import com.musichub.mobile.data.api.models.PairingClaimRequest
import com.musichub.mobile.data.api.models.PlaybackRequest
import com.musichub.mobile.data.api.models.PlaylistDetailResponse
import com.musichub.mobile.data.api.models.PlaylistDto
import com.musichub.mobile.data.api.models.RefreshRequest
import com.musichub.mobile.data.api.models.SearchResponse
import com.musichub.mobile.data.api.models.SessionDto
import com.musichub.mobile.data.api.models.StreamTokenResponse
import com.musichub.mobile.data.api.models.TokenResponse
import com.musichub.mobile.data.api.models.TrackDetailResponse
import com.musichub.mobile.data.api.models.TrackDto
import com.musichub.mobile.data.api.models.UserDto
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * Exact HTTP surface of MusicHub.Api that the Android client uses. Paths are relative so a server
 * hosted under a reverse-proxy sub-path also works.
 */
interface MusicHubApi {

    @POST("api/auth/login")
    suspend fun login(@Body request: LoginRequest): TokenResponse

    @POST("api/auth/refresh")
    suspend fun refresh(@Body request: RefreshRequest): TokenResponse

    @POST("api/auth/logout")
    suspend fun logout(@Body request: RefreshRequest): Response<Unit>

    @POST("api/auth/change-password")
    suspend fun changePassword(@Body request: ChangePasswordRequest): TokenResponse

    @POST("api/auth/logout-all")
    suspend fun logoutAll(): Response<Unit>

    @GET("api/auth/me")
    suspend fun me(): UserDto

    @GET("api/auth/sessions")
    suspend fun sessions(): List<SessionDto>

    @POST("api/pairing/claim")
    suspend fun claimPairing(@Body request: PairingClaimRequest): TokenResponse

    @GET("api/tracks")
    suspend fun tracks(
        @Query("q") query: String? = null,
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 50,
        @Query("sort") sort: String? = null,
        @Query("favorites") favorites: Boolean? = null,
        @Query("albumId") albumId: String? = null,
        @Query("artistId") artistId: String? = null,
        @Query("folderId") folderId: String? = null,
        @Query("genreId") genreId: String? = null,
    ): PagedResult<TrackDto>

    @GET("api/tracks/{id}")
    suspend fun track(@Path("id") id: String): TrackDetailResponse

    @GET("api/albums")
    suspend fun albums(
        @Query("q") query: String? = null,
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 100,
    ): PagedResult<AlbumDto>

    @GET("api/albums/{id}")
    suspend fun album(@Path("id") id: String): AlbumDetailResponse

    @GET("api/artists")
    suspend fun artists(
        @Query("q") query: String? = null,
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 100,
    ): PagedResult<ArtistDto>

    @GET("api/artists/{id}")
    suspend fun artist(@Path("id") id: String): ArtistDetailResponse

    @GET("api/folders")
    suspend fun folders(): List<FolderNodeDto>

    @GET("api/genres")
    suspend fun genres(): List<GenreDto>

    @GET("api/playlists")
    suspend fun playlists(): List<PlaylistDto>

    @GET("api/playlists/{id}")
    suspend fun playlist(@Path("id") id: String): PlaylistDetailResponse

    @POST("api/favorites/{trackId}")
    suspend fun addFavorite(@Path("trackId") trackId: String): Response<Unit>

    @DELETE("api/favorites/{trackId}")
    suspend fun removeFavorite(@Path("trackId") trackId: String): Response<Unit>

    @POST("api/history/playback")
    suspend fun reportPlayback(@Body request: PlaybackRequest): Response<Unit>

    @GET("api/history")
    suspend fun history(@Query("limit") limit: Int = 100): List<HistoryEntryDto>

    @GET("api/home")
    suspend fun home(): HomeResponse

    @GET("api/search")
    suspend fun search(@Query("q") query: String): SearchResponse

    @POST("api/tracks/{id}/stream-token")
    suspend fun streamToken(@Path("id") id: String): StreamTokenResponse

    @GET("api/healthz")
    suspend fun healthz(): Response<Unit>
}
