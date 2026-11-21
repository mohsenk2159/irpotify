package com.musichub.mobile.ui.home

import com.musichub.mobile.data.library.FavoriteWriter
import com.musichub.mobile.data.library.HomeData
import com.musichub.mobile.data.library.HomeDataSource
import com.musichub.mobile.data.local.AlbumEntity
import com.musichub.mobile.data.local.TrackEntity
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * Covers the two behaviours the Android UI relies on and that have no other safety net:
 * pull-to-refresh must not blank the screen, and a failed favourite write must roll back.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class HomeViewModelTest {

    private val dispatcher = UnconfinedTestDispatcher()

    @Before
    fun setUp() {
        Dispatchers.setMain(dispatcher)
    }

    @After
    fun tearDown() {
        Dispatchers.resetMain()
    }

    @Test
    fun `initial load publishes success`() = runTest(dispatcher) {
        val track = track("t1", favorite = false)
        val source = FakeHomeSource(result = Result.success(home(recentlyAdded = listOf(track))))
        val viewModel = HomeViewModel(source, FakeFavoriteWriter())

        val state = viewModel.state.value
        assertTrue("expected Success but was $state", state is HomeState.Success)
        assertEquals(listOf(track), (state as HomeState.Success).data.recentlyAdded)
        assertFalse(state.refreshing)
    }

    @Test
    fun `initial load failure publishes failed`() = runTest(dispatcher) {
        val boom = IllegalStateException("offline")
        val viewModel = HomeViewModel(FakeHomeSource(result = Result.failure(boom)), FakeFavoriteWriter())

        val state = viewModel.state.value
        assertTrue("expected Failed but was $state", state is HomeState.Failed)
        assertEquals(boom, (state as HomeState.Failed).error)
    }

    @Test
    fun `pull to refresh keeps content visible and reports refreshing`() = runTest(dispatcher) {
        val gate = CompletableDeferred<Unit>()
        val first = track("t1", favorite = false, title = "Old")
        val second = track("t2", favorite = false, title = "New")
        val source = FakeHomeSource(result = Result.success(home(recentlyAdded = listOf(first))))
        val viewModel = HomeViewModel(source, FakeFavoriteWriter())
        assertEquals(1, source.calls)

        // Stage the second response behind a gate so the in-flight state is observable.
        source.result = Result.success(home(recentlyAdded = listOf(second)))
        source.gate = gate
        viewModel.refresh()

        val mid = viewModel.state.value
        assertTrue("expected Success while refreshing but was $mid", mid is HomeState.Success)
        assertTrue("refresh must report refreshing", (mid as HomeState.Success).refreshing)
        assertEquals(
            "content must stay on screen during refresh",
            listOf(first),
            mid.data.recentlyAdded,
        )

        gate.complete(Unit)
        advanceUntilIdle()

        val done = viewModel.state.value as HomeState.Success
        assertFalse(done.refreshing)
        assertEquals(listOf(second), done.data.recentlyAdded)
        assertEquals(2, source.calls)
    }

    @Test
    fun `pull to refresh failure keeps previous data instead of erroring`() = runTest(dispatcher) {
        val first = track("t1", favorite = false)
        val source = FakeHomeSource(result = Result.success(home(recentlyAdded = listOf(first))))
        val viewModel = HomeViewModel(source, FakeFavoriteWriter())

        source.result = Result.failure(IllegalStateException("network down"))
        viewModel.refresh()
        advanceUntilIdle()

        val state = viewModel.state.value
        assertTrue("stale data must survive a failed refresh, was $state", state is HomeState.Success)
        assertEquals(listOf(first), (state as HomeState.Success).data.recentlyAdded)
        assertFalse(state.refreshing)
    }

    @Test
    fun `toggle favorite flips optimistically and persists`() = runTest(dispatcher) {
        val track = track("t1", favorite = false)
        val writer = FakeFavoriteWriter()
        val viewModel = HomeViewModel(
            FakeHomeSource(result = Result.success(home(recentlyAdded = listOf(track)))),
            writer,
        )

        viewModel.toggleFavorite("t1")

        val after = viewModel.state.value as HomeState.Success
        assertTrue(after.data.recentlyAdded.single().isFavorite)
        assertEquals(listOf("t1" to true), writer.writes)
    }

    @Test
    fun `favoriting adds the track to the favorites section`() = runTest(dispatcher) {
        val track = track("t1", favorite = false, title = "Song")
        val viewModel = HomeViewModel(
            FakeHomeSource(result = Result.success(home(recentlyAdded = listOf(track)))),
            FakeFavoriteWriter(),
        )

        viewModel.toggleFavorite("t1")

        val data = (viewModel.state.value as HomeState.Success).data
        assertEquals(listOf("t1"), data.favorites.map { it.id })
    }

    @Test
    fun `unfavoriting removes the track from the favorites section`() = runTest(dispatcher) {
        val track = track("t1", favorite = true, title = "Song")
        val viewModel = HomeViewModel(
            FakeHomeSource(
                result = Result.success(home(recentlyAdded = listOf(track), favorites = listOf(track))),
            ),
            FakeFavoriteWriter(),
        )
        assertEquals(1, (viewModel.state.value as HomeState.Success).data.favorites.size)

        viewModel.toggleFavorite("t1")

        val data = (viewModel.state.value as HomeState.Success).data
        assertTrue("favorites must drop the unfavorited track", data.favorites.isEmpty())
        assertFalse(data.recentlyAdded.single().isFavorite)
    }

    @Test
    fun `failed favorite write rolls the heart back`() = runTest(dispatcher) {
        val track = track("t1", favorite = false)
        val writer = FakeFavoriteWriter(result = Result.failure(IllegalStateException("404")))
        val viewModel = HomeViewModel(
            FakeHomeSource(result = Result.success(home(recentlyAdded = listOf(track)))),
            writer,
        )

        viewModel.toggleFavorite("t1")
        advanceUntilIdle()

        val data = (viewModel.state.value as HomeState.Success).data
        assertFalse("a rejected write must not leave a filled heart", data.recentlyAdded.single().isFavorite)
        assertTrue(data.favorites.isEmpty())
    }

    private class FakeHomeSource(var result: Result<HomeData>) : HomeDataSource {
        var calls = 0
        var gate: CompletableDeferred<Unit>? = null

        override suspend fun loadHome(): Result<HomeData> {
            calls++
            gate?.await()
            return result
        }
    }

    private class FakeFavoriteWriter(
        private val result: Result<Unit> = Result.success(Unit),
    ) : FavoriteWriter {
        val writes = mutableListOf<Pair<String, Boolean>>()

        override suspend fun setFavorite(trackId: String, favorite: Boolean): Result<Unit> {
            writes += trackId to favorite
            return result
        }
    }

    private companion object {
        fun track(id: String, favorite: Boolean, title: String = "Title $id") = TrackEntity(
            id = id,
            title = title,
            artist = "Artist",
            album = "Album",
            genre = null,
            durationSeconds = 180.0,
            year = 2020,
            trackNumber = 1,
            discNumber = 1,
            codec = "flac",
            sizeBytes = 1_000,
            sha256 = "hash-$id",
            createdAt = null,
            isFavorite = favorite,
            artworkUrl = null,
            cachedAt = 0L,
        )

        fun home(
            recentlyAdded: List<TrackEntity> = emptyList(),
            recentlyPlayed: List<TrackEntity> = emptyList(),
            mostPlayed: List<TrackEntity> = emptyList(),
            favorites: List<TrackEntity> = emptyList(),
            albums: List<AlbumEntity> = emptyList(),
        ) = HomeData(recentlyAdded, recentlyPlayed, mostPlayed, favorites, albums, fromCache = false)
    }
}
