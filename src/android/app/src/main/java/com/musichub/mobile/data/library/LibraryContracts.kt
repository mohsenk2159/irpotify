package com.musichub.mobile.data.library

/**
 * Narrow seams over [LibraryRepository] used by the ViewModels.
 *
 * Passing these interfaces instead of the whole [com.musichub.mobile.data.AppContainer] keeps the
 * home refresh state machine and the optimistic favourite toggle testable in plain JVM unit tests,
 * where no Android Context and no Room database exist.
 */
fun interface HomeDataSource {
    suspend fun loadHome(): Result<HomeData>
}

fun interface FavoriteWriter {
    suspend fun setFavorite(trackId: String, favorite: Boolean): Result<Unit>
}
