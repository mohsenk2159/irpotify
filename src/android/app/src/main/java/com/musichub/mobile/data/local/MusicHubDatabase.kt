package com.musichub.mobile.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.TypeConverters

@Database(
    entities = [
        TrackEntity::class,
        AlbumEntity::class,
        ArtistEntity::class,
        PlaylistEntity::class,
        PlaylistTrackEntity::class,
        AlbumTrackEntity::class,
        FolderTrackEntity::class,
        FolderEntity::class,
        GenreEntity::class,
        DownloadEntity::class,
        PendingFavoriteEntity::class,
        PendingPlaybackEntity::class,
        HistoryEntity::class,
        PlaybackStateEntity::class,
        QueueItemEntity::class,
    ],
    version = 1,
    exportSchema = true,
)
@TypeConverters(Converters::class)
abstract class MusicHubDatabase : RoomDatabase() {
    abstract fun tracks(): TrackDao
    abstract fun albums(): AlbumDao
    abstract fun artists(): ArtistDao
    abstract fun playlists(): PlaylistDao
    abstract fun folders(): FolderDao
    abstract fun genres(): GenreDao
    abstract fun downloads(): DownloadDao
    abstract fun sync(): SyncDao
    abstract fun history(): HistoryDao
    abstract fun playbackState(): PlaybackStateDao
    abstract fun queue(): QueueDao
    abstract fun cacheAdmin(): CacheAdminDao

    companion object {
        fun build(context: Context): MusicHubDatabase =
            Room.databaseBuilder(context, MusicHubDatabase::class.java, "musichub.db")
                .fallbackToDestructiveMigrationOnDowngrade()
                .build()
    }
}
