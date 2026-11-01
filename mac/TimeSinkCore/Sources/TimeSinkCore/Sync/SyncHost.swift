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
            self?.timer?.invalidate()
            self?.timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
                self?.performSync()
            }
        }

        performSync()
    }

    public func stop() {
        timer?.invalidate()
        timer = nil

        folderSource?.cancel()
        folderSource = nil
        if folderFileDescriptor >= 0 {
            close(folderFileDescriptor)
            folderFileDescriptor = -1
        }
    }

    public func syncNow() {
        performSync()
    }

    public func performSync() {
        lock.lock()
        defer { lock.unlock() }

        guard isEnabled, let folder = syncFolder, !folder.isEmpty else { return }
        let fm = FileManager.default
        guard fm.fileExists(atPath: folder) else { return }

        // 1. Export local snapshot
        do {
            _ = try SyncWriter.write(db: db, syncFolder: folder)
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

                _ = try SyncReader.import(syncFile: fullPath, db: db, timeZone: timeZone)
            }
        } catch {
            NSLog("SyncReader scan error: \(error)")
        }

        onSyncCompleted?()
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
        folderSource?.cancel()
        if folderFileDescriptor >= 0 {
            close(folderFileDescriptor)
        }

        folderFileDescriptor = open(folder, O_EVTONLY)
        guard folderFileDescriptor >= 0 else { return }

        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: folderFileDescriptor,
            eventMask: [.write, .extend, .attrib, .link],
            queue: DispatchQueue.global(qos: .utility)
        )

        source.setEventHandler { [weak self] in
            self?.performSync()
        }

        source.setCancelHandler { [weak self] in
            if let fd = self?.folderFileDescriptor, fd >= 0 {
                close(fd)
                self?.folderFileDescriptor = -1
            }
        }

        self.folderSource = source
        source.resume()
    }
}
