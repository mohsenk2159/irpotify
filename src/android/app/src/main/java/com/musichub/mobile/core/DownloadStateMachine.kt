package com.musichub.mobile.core

/**
 * Download status values persisted in Room and reported to WorkManager observers.
 */
enum class DownloadStatus {
    QUEUED,
    RUNNING,
    PAUSED,
    VERIFYING,
    COMPLETED,
    FAILED,
    CANCELLED;

    val isTerminal: Boolean
        get() = this == COMPLETED || this == FAILED || this == CANCELLED
}

/**
 * Explicit state machine for offline downloads. Every status write goes through this object so an
 * invalid transition cannot silently corrupt the persisted state (for example a completed download
 * being marked as still running).
 */
object DownloadStateMachine {
    private val transitions: Map<DownloadStatus, Set<DownloadStatus>> = mapOf(
        DownloadStatus.QUEUED to setOf(
            DownloadStatus.RUNNING,
            DownloadStatus.PAUSED,
            DownloadStatus.FAILED,
            DownloadStatus.CANCELLED,
        ),
        DownloadStatus.RUNNING to setOf(
            DownloadStatus.VERIFYING,
            DownloadStatus.PAUSED,
            DownloadStatus.FAILED,
            DownloadStatus.CANCELLED,
        ),
        DownloadStatus.PAUSED to setOf(
            DownloadStatus.QUEUED,
            DownloadStatus.RUNNING,
            DownloadStatus.CANCELLED,
        ),
        DownloadStatus.VERIFYING to setOf(
            DownloadStatus.COMPLETED,
            DownloadStatus.FAILED,
            DownloadStatus.CANCELLED,
        ),
        DownloadStatus.FAILED to setOf(
            DownloadStatus.QUEUED,
            DownloadStatus.CANCELLED,
        ),
        DownloadStatus.COMPLETED to emptySet(),
        DownloadStatus.CANCELLED to setOf(DownloadStatus.QUEUED),
    )

    fun canTransition(from: DownloadStatus, to: DownloadStatus): Boolean =
        transitions[from]?.contains(to) == true

    fun transition(from: DownloadStatus, to: DownloadStatus): DownloadStatus {
        require(canTransition(from, to)) { "Illegal download transition: $from -> $to" }
        return to
    }

    /**
     * A retry is only valid from a failed or paused status. Completed downloads must be deleted
     * and re-created instead of retried, which prevents overwriting a verified file.
     */
    fun canRetry(status: DownloadStatus): Boolean =
        status == DownloadStatus.FAILED || status == DownloadStatus.PAUSED || status == DownloadStatus.CANCELLED
}
