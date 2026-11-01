import Foundation
import AppKit

/// Process provider querying NSWorkspace running applications and tracking launch/quit notifications.
public final class NSWorkspaceProcessProvider: ProcessProvider, @unchecked Sendable {
    private let lock = NSLock()
    private var processes: [pid_t: ProcInfo] = [:]
    private var observers: [NSObjectProtocol] = []

    public var onProcessChanged: (@Sendable () -> Void)?

    public init(autoObserve: Bool = true) {
        if autoObserve {
            scanRunningApplications()
            setupNotificationObservers()
        }
    }

    deinit {
        for obs in observers {
            NSWorkspace.shared.notificationCenter.removeObserver(obs)
        }
    }

    public func scanRunningApplications() {
        lock.lock()
        defer { lock.unlock() }

        processes.removeAll()
        for app in NSWorkspace.shared.runningApplications {
            guard let info = makeProcInfo(from: app) else { continue }
            processes[app.processIdentifier] = info
        }
    }

    public func addOrUpdate(pid: pid_t, info: ProcInfo) {
        lock.lock()
        processes[pid] = info
        lock.unlock()
        onProcessChanged?()
    }

    public func remove(pid: pid_t) {
        lock.lock()
        processes.removeValue(forKey: pid)
        lock.unlock()
        onProcessChanged?()
    }

    public func snapshot() -> [ProcInfo] {
        lock.lock()
        defer { lock.unlock() }
        return Array(processes.values)
    }

    private func setupNotificationObservers() {
        let wsCenter = NSWorkspace.shared.notificationCenter

        let launchObs = wsCenter.addObserver(
            forName: NSWorkspace.didLaunchApplicationNotification,
            object: nil,
            queue: nil
        ) { [weak self] note in
            guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication,
                  let info = self?.makeProcInfo(from: app) else { return }
            self?.addOrUpdate(pid: app.processIdentifier, info: info)
        }

        let termObs = wsCenter.addObserver(
            forName: NSWorkspace.didTerminateApplicationNotification,
            object: nil,
            queue: nil
        ) { [weak self] note in
            guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication else { return }
            self?.remove(pid: app.processIdentifier)
        }

        observers.append(contentsOf: [launchObs, termObs])
    }

    private func makeProcInfo(from app: NSRunningApplication) -> ProcInfo? {
        let pid = app.processIdentifier
        let bundleId = app.bundleIdentifier
        let fullPath = app.executableURL?.path
        let name = app.localizedName ?? bundleId ?? app.executableURL?.lastPathComponent ?? "Unknown"
        let startUtc = app.launchDate ?? Date()

        return ProcInfo(
            pid: pid,
            name: name,
            fullPath: fullPath,
            startUtc: startUtc,
            bundleId: bundleId
        )
    }
}

