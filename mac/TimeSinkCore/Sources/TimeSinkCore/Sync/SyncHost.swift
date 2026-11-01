import Foundation

public enum SyncDeviceStatus: String, Sendable, Equatable {
    case upToDate = "Up to date"
    case synced = "Synced"
    case pending = "Pending"
    case onlyTwoDevices = "Only two devices are supported yet"
    case noOtherFound = "No other device found"
    case disabled = "Sync disabled"
}

public final class SyncHost: @unchecked Sendable {
    private let db: SQLiteDatabase
    private let timeZone: TimeZone
    private var timer: Timer?
    private var folderSource: DispatchSourceFileSystemObject?
    private var folderFileDescriptor: Int32 = -1
    private let lock = NSLock()

    // Watcher debouncing & loop prevention
    private let debounceQueue = DispatchQueue(label: "com.timesink.sync.debounce", qos: .utility)
    private var debounceWorkItem: DispatchWorkItem?
    private var lastLocalWriteUtc: TimeInterval = 0

    public var onSyncStarted: (@Sendable () -> Void)?
    public var onSyncCompleted: (@Sendable () -> Void)?

    public init(db: SQLiteDatabase, timeZone: TimeZone = .current) {
        self.db = db
        self.timeZone = timeZone
    }

    deinit {
        stop()
    }

    public var isEnabled: Bool {
        (try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.enabled';"))?.lowercased() == "true"
    }

    public var syncFolder: String? {
        try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.folder';")
    }

    public func start() {
        guard isEnabled, let folder = syncFolder, !folder.isEmpty else { return }
        setupWatcher(folder: folder)

        // Periodic export/import every 5 minutes
        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }
            self.timer?.invalidate()
            let t = Timer(timeInterval: 300, repeats: true) { [weak self] _ in
                DispatchQueue.global(qos: .utility).async {
                    self?.performSync()
                }
            }
            RunLoop.main.add(t, forMode: .common)
            self.timer = t
        }

        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.performSync()
        }
    }

    public func stop() {
        DispatchQueue.main.async { [weak self] in
            self?.timer?.invalidate()
            self?.timer = nil
        }

        stopWatcher()
    }

    public func restart() {
        stop()
        start()
    }

    public func syncNow() {
        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.performSync()
        }
    }

    public func performSync() {
        lock.lock()
        defer { lock.unlock() }

        guard isEnabled, let folder = syncFolder, !folder.isEmpty else { return }
        let fm = FileManager.default
        guard fm.fileExists(atPath: folder) else { return }

        DispatchQueue.main.async { [weak self] in
            self?.onSyncStarted?()
        }

        var exportSucceeded = false
        var importSucceeded = false

        // 1. Export local snapshot
        do {
            _ = try SyncWriter.write(db: db, syncFolder: folder)
            exportSucceeded = true
            self.lastLocalWriteUtc = Date().timeIntervalSince1970
            let nowUnix = Int64(Date().timeIntervalSince1970)
            try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.last_export_utc', '\(nowUnix)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        } catch {
            NSLog("SyncWriter error: \(error)")
        }

        // 2. Scan and import other devices' snapshots
        do {
            let files = try fm.contentsOfDirectory(atPath: folder)
            let localDevId = (try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';")) ?? ""

            for file in files where file.hasSuffix(SyncWriter.extension) && !file.hasSuffix(".tmp") {
                let fullPath = (folder as NSString).appendingPathComponent(file)
                let baseName = (file as NSString).deletingPathExtension
                if baseName.caseInsensitiveCompare(localDevId) == .orderedSame {
                    continue
                }

                let result = try SyncReader.import(syncFile: fullPath, db: db, timeZone: timeZone)
                if result.status == .success {
                    importSucceeded = true
                }
            }

            if importSucceeded {
                let nowUnix = Int64(Date().timeIntervalSince1970)
                try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.last_import_utc', '\(nowUnix)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            }
        } catch {
            NSLog("SyncReader scan error: \(error)")
        }

        // 3. Update sync.state in settings
        let status = getDeviceStatus()
        let stateStr: String
        switch status {
        case .noOtherFound: stateStr = "no_other_device"
        case .pending: stateStr = "pending"
        case .onlyTwoDevices: stateStr = "only_two_devices"
        case .synced, .upToDate: stateStr = "synced"
        case .disabled: stateStr = "disabled"
        }
        try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.state', '\(stateStr)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")

        // 4. Notify on main thread
        DispatchQueue.main.async { [weak self] in
            self?.onSyncCompleted?()
        }
    }

    public func getDeviceStatus() -> SyncDeviceStatus {
        guard isEnabled else { return .disabled }
        guard let folder = syncFolder, !folder.isEmpty else { return .disabled }

        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(atPath: folder) else {
            return .noOtherFound
        }

        let localDevId = (try? db.scalarText("SELECT value FROM settings WHERE key = 'device.id';")) ?? ""
        let syncFiles = files.filter { $0.hasSuffix(SyncWriter.extension) && !$0.hasSuffix(".tmp") }
        let otherFiles = syncFiles.filter {
            (($0 as NSString).deletingPathExtension).caseInsensitiveCompare(localDevId) != .orderedSame
        }

        if otherFiles.count > 1 {
            return .onlyTwoDevices
        }

        guard let remoteFile = otherFiles.first else {
            return .noOtherFound
        }

        let remotePath = (folder as NSString).appendingPathComponent(remoteFile)
        if SyncReader.isPlaceholder(filePath: remotePath) {
            return .pending
        }

        return .upToDate
    }

    private func setupWatcher(folder: String) {
        stopWatcher()

        folderFileDescriptor = open(folder, O_EVTONLY)
        guard folderFileDescriptor >= 0 else { return }

        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: folderFileDescriptor,
            eventMask: [.write, .extend, .attrib, .link],
            queue: debounceQueue
        )

        source.setEventHandler { [weak self] in
            guard let self = self else { return }
            self.debounceWorkItem?.cancel()
            let item = DispatchWorkItem { [weak self] in
                guard let self = self else { return }
                // Suppress events caused by our own recent write
                if Date().timeIntervalSince1970 - self.lastLocalWriteUtc < 3.0 {
                    return
                }
                self.performSync()
            }
            self.debounceWorkItem = item
            self.debounceQueue.asyncAfter(deadline: .now() + 1.5, execute: item)
        }

        source.setCancelHandler { [weak self] in
            guard let self = self else { return }
            if self.folderFileDescriptor >= 0 {
                close(self.folderFileDescriptor)
                self.folderFileDescriptor = -1
            }
        }

        self.folderSource = source
        source.resume()
    }

    private func stopWatcher() {
        debounceWorkItem?.cancel()
        debounceWorkItem = nil

        if let source = folderSource {
            source.cancel()
            folderSource = nil
        } else if folderFileDescriptor >= 0 {
            close(folderFileDescriptor)
            folderFileDescriptor = -1
        }
    }
}
