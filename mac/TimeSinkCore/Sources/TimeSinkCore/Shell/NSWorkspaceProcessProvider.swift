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
            guard !app.isTerminated, let info = makeProcInfo(from: app) else { continue }
            processes[app.processIdentifier] = info
        }
    }

    /// Actively reconciles the cached process list with NSWorkspace.shared.runningApplications
    /// and prunes dead or terminated processes even if quit notifications were dropped.
    public func reconcileRunningApplications() {
        lock.lock()
        var changed = false
        let runningApps = NSWorkspace.shared.runningApplications
        var currentPids = Set<pid_t>()

        for app in runningApps {
            if app.isTerminated { continue }
            let pid = app.processIdentifier
            currentPids.insert(pid)

            if let existing = processes[pid] {
                if existing.isActive != app.isActive {
                    processes[pid] = ProcInfo(
                        pid: existing.pid,
                        name: existing.name,
                        fullPath: existing.fullPath,
                        startUtc: existing.startUtc,
                        packageFamily: existing.packageFamily,
                        bundleId: existing.bundleId,
                        isActive: app.isActive,
                        activeSinceUtc: app.isActive ? (existing.activeSinceUtc ?? Date()) : nil
                    )
                    changed = true
                }
            } else if let info = makeProcInfo(from: app) {
                processes[pid] = info
                changed = true
            }
        }

        for pid in Array(processes.keys) {
            if !currentPids.contains(pid) {
                processes.removeValue(forKey: pid)
                changed = true
            } else if kill(pid, 0) != 0 && errno == ESRCH {
                processes.removeValue(forKey: pid)
                changed = true
            }
        }
        lock.unlock()

        if changed {
            onProcessChanged?()
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

    public func handleActivation(pid: pid_t, at date: Date = Date()) {
        lock.lock()
        for (p, info) in processes {
            if p == pid {
                processes[p] = ProcInfo(
                    pid: info.pid,
                    name: info.name,
                    fullPath: info.fullPath,
                    startUtc: info.startUtc,
                    packageFamily: info.packageFamily,
                    bundleId: info.bundleId,
                    isActive: true,
                    activeSinceUtc: date
                )
            } else if info.isActive {
                processes[p] = ProcInfo(
                    pid: info.pid,
                    name: info.name,
                    fullPath: info.fullPath,
                    startUtc: info.startUtc,
                    packageFamily: info.packageFamily,
                    bundleId: info.bundleId,
                    isActive: false,
                    activeSinceUtc: nil
                )
            }
        }
        lock.unlock()
        onProcessChanged?()
    }

    public func handleDeactivation(pid: pid_t) {
        lock.lock()
        if let info = processes[pid], info.isActive {
            processes[pid] = ProcInfo(
                pid: info.pid,
                name: info.name,
                fullPath: info.fullPath,
                startUtc: info.startUtc,
                packageFamily: info.packageFamily,
                bundleId: info.bundleId,
                isActive: false,
                activeSinceUtc: nil
            )
        }
        lock.unlock()
        onProcessChanged?()
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

        let actObs = wsCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil,
            queue: nil
        ) { [weak self] note in
            guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication else { return }
            self?.handleActivation(pid: app.processIdentifier)
        }

        let deactObs = wsCenter.addObserver(
            forName: NSWorkspace.didDeactivateApplicationNotification,
            object: nil,
            queue: nil
        ) { [weak self] note in
            guard let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication else { return }
            self?.handleDeactivation(pid: app.processIdentifier)
        }

        observers.append(contentsOf: [launchObs, termObs, actObs, deactObs])
    }

    private func makeProcInfo(from app: NSRunningApplication) -> ProcInfo? {
        let pid = app.processIdentifier
        let bundleId = app.bundleIdentifier
        let fullPath = app.executableURL?.path
        let name = app.localizedName ?? bundleId ?? app.executableURL?.lastPathComponent ?? "Unknown"
        let startUtc = app.launchDate ?? Date()
        let isActive = app.isActive

        return ProcInfo(
            pid: pid,
            name: name,
            fullPath: fullPath,
            startUtc: startUtc,
            bundleId: bundleId,
            isActive: isActive,
            activeSinceUtc: isActive ? Date() : nil
        )
    }
}

