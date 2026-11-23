import Foundation
import AVFoundation
import MediaPlayer
import Combine

public final class AudioPlayerService: ObservableObject {
    public static let shared = AudioPlayerService()

    @Published public var currentTrack: TrackDto? = nil
    @Published public var isPlaying: Bool = false
    @Published public var currentTime: Double = 0.0
    @Published public var duration: Double = 0.0
    @Published public var queue: [TrackDto] = []
    @Published public var currentIndex: Int = 0
    @Published public var isShuffle: Bool = false
    @Published public var isRepeat: Bool = false

    private var player: AVPlayer?
    private var timeObserverToken: Any?
    private var cancellables = Set<AnyCancellable>()

    private init() {
        configureAudioSession()
        configureRemoteCommands()
    }

    private func configureAudioSession() {
        do {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .default, policy: .longFormAudio, options: [])
            try session.setActive(true)
        } catch {
            print("Failed to set audio session category: \(error)")
        }
    }

    // MARK: - Queue & Playback

    public func playTrack(_ track: TrackDto, in queue: [TrackDto] = []) {
        if !queue.isEmpty {
            self.queue = queue
            if let idx = queue.firstIndex(where: { $0.id == track.id }) {
                self.currentIndex = idx
            }
        } else if !self.queue.contains(where: { $0.id == track.id }) {
            self.queue = [track]
            self.currentIndex = 0
        }

        self.currentTrack = track
        self.duration = track.durationSeconds
        self.currentTime = 0.0

        guard let streamUrl = NetworkService.shared.getAudioStreamUrl(trackId: track.id) else {
            return
        }

        playUrl(streamUrl, track: track)
    }

    private func playUrl(_ url: URL, track: TrackDto) {
        removeTimeObserver()
        NotificationCenter.default.removeObserver(self, name: .AVPlayerItemDidPlayToEndTime, object: player?.currentItem)

        let playerItem = AVPlayerItem(url: url)
        if player == nil {
            player = AVPlayer(playerItem: playerItem)
            player?.automaticallyWaitsToMinimizeStalling = true
        } else {
            player?.replaceCurrentItem(with: playerItem)
        }

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(playerItemDidReachEnd),
            name: .AVPlayerItemDidPlayToEndTime,
            object: playerItem
        )

        addPeriodicTimeObserver()

        player?.play()
        self.isPlaying = true
        updateNowPlayingInfo(track: track)
    }

    public func togglePlayPause() {
        guard let p = player else { return }
        if isPlaying {
            p.pause()
            isPlaying = false
        } else {
            p.play()
            isPlaying = true
        }
        updatePlaybackRateInNowPlaying()
    }

    public func next() {
        guard !queue.isEmpty else { return }
        if isShuffle {
            currentIndex = Int.random(in: 0..<queue.count)
        } else {
            currentIndex = (currentIndex + 1) % queue.count
        }
        playTrack(queue[currentIndex])
    }

    public func previous() {
        guard !queue.isEmpty else { return }
        if currentTime > 3.0 {
            seek(to: 0)
            return
        }
        if currentIndex > 0 {
            currentIndex -= 1
        } else {
            currentIndex = queue.count - 1
        }
        playTrack(queue[currentIndex])
    }

    public func seek(to seconds: Double) {
        let targetTime = CMTime(seconds: seconds, preferredTimescale: 600)
        player?.seek(to: targetTime, toleranceBefore: .zero, toleranceAfter: .zero) { [weak self] _ in
            self?.currentTime = seconds
            self?.updateNowPlayingElapsed()
        }
    }

    @objc private func playerItemDidReachEnd() {
        if let current = currentTrack {
            Task {
                await NetworkService.shared.reportPlayback(trackId: current.id, position: duration, completed: true)
            }
        }

        if isRepeat {
            seek(to: 0)
            player?.play()
            isPlaying = true
        } else {
            next()
        }
    }

    // MARK: - Time Observer

    private func addPeriodicTimeObserver() {
        let interval = CMTime(seconds: 0.5, preferredTimescale: 600)
        timeObserverToken = player?.addPeriodicTimeObserver(forInterval: interval, queue: .main) { [weak self] time in
            guard let self = self else { return }
            let sec = time.seconds
            if sec.isFinite && !sec.isNaN {
                self.currentTime = sec
                if let item = self.player?.currentItem {
                    let d = item.duration.seconds
                    if d.isFinite && !d.isNaN && d > 0 {
                        self.duration = d
                    }
                }
            }
        }
    }

    private func removeTimeObserver() {
        if let token = timeObserverToken {
            player?.removeTimeObserver(token)
            timeObserverToken = nil
        }
    }

    // MARK: - Lock Screen & Control Center (MPNowPlayingInfoCenter)

    private func configureRemoteCommands() {
        let center = MPRemoteCommandCenter.shared()

        center.playCommand.isEnabled = true
        center.playCommand.addTarget { [weak self] _ in
            self?.player?.play()
            self?.isPlaying = true
            self?.updatePlaybackRateInNowPlaying()
            return .success
        }

        center.pauseCommand.isEnabled = true
        center.pauseCommand.addTarget { [weak self] _ in
            self?.player?.pause()
            self?.isPlaying = false
            self?.updatePlaybackRateInNowPlaying()
            return .success
        }

        center.togglePlayPauseCommand.isEnabled = true
        center.togglePlayPauseCommand.addTarget { [weak self] _ in
            self?.togglePlayPause()
            return .success
        }

        center.nextTrackCommand.isEnabled = true
        center.nextTrackCommand.addTarget { [weak self] _ in
            self?.next()
            return .success
        }

        center.previousTrackCommand.isEnabled = true
        center.previousTrackCommand.addTarget { [weak self] _ in
            self?.previous()
            return .success
        }

        center.changePlaybackPositionCommand.isEnabled = true
        center.changePlaybackPositionCommand.addTarget { [weak self] event in
            if let posEvent = event as? MPChangePlaybackPositionCommandEvent {
                self?.seek(to: posEvent.positionTime)
                return .success
            }
            return .commandFailed
        }
    }

    private func updateNowPlayingInfo(track: TrackDto) {
        var info: [String: Any] = [
            MPMediaItemPropertyTitle: track.title,
            MPMediaItemPropertyArtist: track.artist,
            MPMediaItemPropertyAlbumTitle: track.album,
            MPMediaItemPropertyPlaybackDuration: track.durationSeconds,
            MPNowPlayingInfoPropertyElapsedPlaybackTime: 0.0,
            MPNowPlayingInfoPropertyPlaybackRate: 1.0
        ]

        if let artUrl = NetworkService.shared.getArtworkUrl(path: track.artworkUrl) {
            URLSession.shared.dataTask(with: artUrl) { data, _, _ in
                if let data = data, let image = UIImage(data: data) {
                    let artwork = MPMediaItemArtwork(boundsSize: image.size) { _ in image }
                    DispatchQueue.main.async {
                        var currentInfo = MPNowPlayingInfoCenter.default().nowPlayingInfo ?? [:]
                        currentInfo[MPMediaItemPropertyArtwork] = artwork
                        MPNowPlayingInfoCenter.default().nowPlayingInfo = currentInfo
                    }
                }
            }.resume()
        }

        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }

    private func updatePlaybackRateInNowPlaying() {
        var currentInfo = MPNowPlayingInfoCenter.default().nowPlayingInfo ?? [:]
        currentInfo[MPNowPlayingInfoPropertyPlaybackRate] = isPlaying ? 1.0 : 0.0
        currentInfo[MPNowPlayingInfoPropertyElapsedPlaybackTime] = currentTime
        MPNowPlayingInfoCenter.default().nowPlayingInfo = currentInfo
    }

    private func updateNowPlayingElapsed() {
        var currentInfo = MPNowPlayingInfoCenter.default().nowPlayingInfo ?? [:]
        currentInfo[MPNowPlayingInfoPropertyElapsedPlaybackTime] = currentTime
        MPNowPlayingInfoCenter.default().nowPlayingInfo = currentInfo
    }
}
