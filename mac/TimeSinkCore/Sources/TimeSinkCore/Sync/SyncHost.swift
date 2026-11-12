import Foundation

public enum SyncDeviceStatus: String, Sendable, Equatable {
    case upToDate = "Up to date"
    case synced = "Synced"
    case pending = "Pending"
    case onlyTwoDevices = "Only two devices are supported yet"
    case noOtherFound = "No other device found"
    case disabled = "Sync disabled"
    case encryptionKeyMissing = "Passphrase required"
    case encryptionKeyWrong = "Passphrase mismatch"
    case remoteNotEncrypted = "Remote device unencrypted"
    case exportFailed = "Couldn't write sync file"
}

public final class SyncHost: @unchecked Sendable {
    // `sharedDb` is set by init(db:) (in-memory tests): one connection does everything.
    // Otherwise (init(dbPath:)) sync owns a private connection, `syncDb`, opened lazily under `lock`,
    // so ATTACH/BEGIN here can never interleave with the tracker/UI connection.
    private let sharedDb: SQLiteDatabase?
    private let dbPath: String?
    private let dbKey: Data?
    private var syncDb: SQLiteDatabase?   // guarded by `lock`
    private var readDb: SQLiteDatabase?   // settings reads from other threads; guarded by `readLock`
    private let readLock = NSLock()
    private let timeZone: TimeZone
    private let keyManager: KeyManagerProtocol
    private var timer: Timer?
    private var folderSource: DispatchSourceFileSystemObject?
    private var folderFileDescriptor: Int32 = -1
    private let lock = NSLock()
    private var isPausedDueToEncryptionError: Bool = false
    private var unchangedStamps: [String: String] = [:]
    private var fingerprintCount: Int64 = -1

    // Export failure retry: one quick retry after `exportRetryDelay`, then the 5-minute timer.
    var exportRetryDelay: TimeInterval = 30
    private var exportFailureStreak = 0                  // guarded by `lock`
    private var exportRetryItem: DispatchWorkItem?

    // Watcher debouncing & loop prevention
    private let debounceQueue = DispatchQueue(label: "com.timesink.sync.debounce", qos: .utility)
    private var debounceWorkItem: DispatchWorkItem?
    private var lastLocalWriteUtc: TimeInterval = 0

    public var onSyncStarted: (@Sendable () -> Void)?
    public var onSyncCompleted: (@Sendable () -> Void)?

    public init(
        db: SQLiteDatabase,
        timeZone: TimeZone = .current,
        keyManager: KeyManagerProtocol = KeychainKeyManager.shared
    ) {
        self.sharedDb = db
        self.dbPath = nil
        self.dbKey = nil
        self.timeZone = timeZone
        self.keyManager = keyManager
    }

    /// Sync runs on its own connection to the database file at `dbPath`; the schema must already be migrated.
    public init(
        dbPath: String,
        key: Data?,
        timeZone: TimeZone = .current,
        keyManager: KeyManagerProtocol = KeychainKeyManager.shared
    ) {
        self.sharedDb = nil
        self.dbPath = dbPath
        self.dbKey = key
        self.timeZone = timeZone
        self.keyManager = keyManager
    }

    /// Must be called with `lock` held.
    private func syncConnection() throws -> SQLiteDatabase {
        if let shared = sharedDb { return shared }
        if let existing = syncDb { return existing }
        guard let path = dbPath else { throw SQLiteError.generic("SyncHost has no database") }
        let conn = try SQLiteDatabase(path: path, key: dbKey)
        syncDb = conn
        return conn
    }

    /// Drops the private connection (and any leaked attach/transaction); reopened on the next run.
    /// Must be called with `lock` held.
    private func resetSyncConnection() {
        guard sharedDb == nil else { return }
        syncDb?.close()
        syncDb = nil
    }

    private func settingsConnection() -> SQLiteDatabase? {
        if let shared = sharedDb { return shared }
        readLock.lock()
        defer { readLock.unlock() }
        if readDb == nil, let path = dbPath {
            do {
                readDb = try SQLiteDatabase(path: path, key: dbKey)
            } catch {
                NSLog("SyncHost settings connection error: \(error)")
            }
        }
        return readDb
    }

    deinit {
        stop()
    }

    public var isEnabled: Bool {
        guard let db = settingsConnection() else { return false }
        return (try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.enabled';"))?.lowercased() == "true"
    }

    public var syncFolder: String? {
        guard let db = settingsConnection() else { return nil }
        return try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.folder';")
    }

    public func start() {
        isPausedDueToEncryptionError = false
        guard isEnabled, let folder = syncFolder, !folder.isEmpty else { return }
        setupWatcher(folder: folder)

        // Periodic export/import every 5 minutes
        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }
            self.timer?.invalidate()
            let t = Timer(timeInterval: 300, repeats: true) { [weak self] _ in
                guard let self = self else { return }
                DispatchQueue.global(qos: .utility).async { [weak self] in
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
        isPausedDueToEncryptionError = false
        stop()
        start()
    }

    public func syncNow() {
        isPausedDueToEncryptionError = false
        DispatchQueue.global(qos: .utility).async { [weak self] in
            self?.performSync()
        }
    }

    public func performSync() {
        lock.lock()
        defer { lock.unlock() }

        if isPausedDueToEncryptionError {
            return
        }

        guard isEnabled, let folder = syncFolder, !folder.isEmpty else { return }
        let fm = FileManager.default
        guard fm.fileExists(atPath: folder) else { return }

        let db: SQLiteDatabase
        do {
            db = try syncConnection()
        } catch {
            NSLog("SyncHost connection error: \(error)")
            return
        }
        var needsReconnect = false

        DispatchQueue.main.async { [weak self] in
            self?.onSyncStarted?()
        }

        var importSucceeded = false
        var encryptionError: SyncStatus? = nil
        var exportError: String? = nil

        // 1. Export local snapshot
        do {
            _ = try SyncWriter.write(db: db, syncFolder: folder, keyManager: keyManager)
            self.lastLocalWriteUtc = Date().timeIntervalSince1970
            let nowUnix = Int64(Date().timeIntervalSince1970)
            try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.last_export_utc', '\(nowUnix)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            exportFailureStreak = 0
            // Drop the stale failure marker so getDeviceStatus() does not read it back.
            try? db.exec("UPDATE settings SET value = 'pending' WHERE key = 'sync.state' AND value = 'export_failed'; DELETE FROM settings WHERE key = 'sync.last_error';")
        } catch {
            NSLog("SyncWriter error: \(error)")
            needsReconnect = true
            let short = SyncExportHealth.shortErrorText(error)
            exportError = short
            try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.last_error', '\(short)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            exportFailureStreak += 1
            if exportFailureStreak == 1 { scheduleExportRetry() }
        }

        // 2. Scan and import other devices' snapshots
        do {
            let files = try fm.contentsOfDirectory(atPath: folder)
            let localDevId = (try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';")) ?? ""

            // Fingerprint rows are cleared by forget/delete/passphrase change; a changed count drops the skip cache.
            let fpCount = (try db.scalarInt("SELECT COUNT(*) FROM settings WHERE key LIKE 'sync.imported.%';")) ?? 0
            if fpCount != fingerprintCount {
                unchangedStamps.removeAll()
                fingerprintCount = fpCount
            }

            for file in files where file.hasSuffix(SyncWriter.extension) && !file.hasSuffix(".tmp") {
                let fullPath = (folder as NSString).appendingPathComponent(file)
                let baseName = (file as NSString).deletingPathExtension
                if baseName.caseInsensitiveCompare(localDevId) == .orderedSame {
                    continue
                }

                let attrs = try? fm.attributesOfItem(atPath: fullPath)
                let stamp = "\(attrs?[.size] ?? 0):\((attrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0)"
                if unchangedStamps[fullPath] == stamp { continue }

                let result = try SyncReader.import(syncFile: fullPath, db: db, timeZone: timeZone, keyManager: keyManager)
                if result.status == .unchanged {
                    unchangedStamps[fullPath] = stamp
                } else if result.status == .success {
                    importSucceeded = true
                } else if result.status == .encryptionKeyMissing ||
                           result.status == .encryptionKeyWrong ||
                           result.status == .remoteNotEncrypted {
                    encryptionError = result.status
                    break
                }
            }

            if importSucceeded {
                let nowUnix = Int64(Date().timeIntervalSince1970)
                try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.last_import_utc', '\(nowUnix)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            }
        } catch {
            NSLog("SyncReader scan error: \(error)")
            needsReconnect = true
        }

        // 3. Update sync.state in settings and pause retries on encryption error
        let stateStr: String
        if let err = encryptionError {
            self.isPausedDueToEncryptionError = true
            switch err {
            case .encryptionKeyMissing:
                stateStr = "encryption_key_missing"
            case .encryptionKeyWrong:
                stateStr = "encryption_key_wrong"
            case .remoteNotEncrypted:
                stateStr = "remote_not_encrypted"
            default:
                stateStr = "failed"
            }
        } else if exportError != nil {
            stateStr = "export_failed"
        } else {
            let status = getDeviceStatus()
            switch status {
            case .noOtherFound: stateStr = "no_other_device"
            case .pending: stateStr = "pending"
            case .onlyTwoDevices: stateStr = "only_two_devices"
            case .synced, .upToDate: stateStr = "synced"
            case .disabled: stateStr = "disabled"
            case .encryptionKeyMissing: stateStr = "encryption_key_missing"
            case .encryptionKeyWrong: stateStr = "encryption_key_wrong"
            case .remoteNotEncrypted: stateStr = "remote_not_encrypted"
            case .exportFailed: stateStr = "export_failed"
            }
        }
        try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.state', '\(stateStr)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")

        // A thrown error may have left an attach or transaction open; start clean next run.
        if needsReconnect { resetSyncConnection() }

        // 4. Notify on main thread
        DispatchQueue.main.async { [weak self] in
            self?.onSyncCompleted?()
        }
    }

    public func getDeviceStatus() -> SyncDeviceStatus {
        guard isEnabled else { return .disabled }
        guard let folder = syncFolder, !folder.isEmpty else { return .disabled }
        guard let db = settingsConnection() else { return .disabled }

        let currentState = (try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.state';")) ?? ""
        if currentState == "encryption_key_missing" {
            return .encryptionKeyMissing
        } else if currentState == "encryption_key_wrong" {
            return .encryptionKeyWrong
        } else if currentState == "remote_not_encrypted" {
            return .remoteNotEncrypted
        } else if currentState == "export_failed" {
            return .exportFailed
        }

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

    /// Called with `lock` held after the first failed export of a streak.
    private func scheduleExportRetry() {
        exportRetryItem?.cancel()
        let item = DispatchWorkItem { [weak self] in
            self?.performSync()
        }
        exportRetryItem = item
        debounceQueue.asyncAfter(deadline: .now() + exportRetryDelay, execute: item)
    }

    private func stopWatcher() {
        exportRetryItem?.cancel()
        exportRetryItem = nil
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
