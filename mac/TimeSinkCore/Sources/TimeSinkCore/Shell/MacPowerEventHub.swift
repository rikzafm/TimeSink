import Foundation
import AppKit

/// Observes macOS system power events and screen lock/unlock notifications, routing them to PowerEventHub.
public final class MacPowerEventHub: @unchecked Sendable {
    public let hub: PowerEventHub
    private var observers: [NSObjectProtocol] = []

    public init(hub: PowerEventHub = PowerEventHub(), autoObserve: Bool = true) {
        self.hub = hub
        if autoObserve {
            setupObservers()
        }
    }

    deinit {
        for obs in observers {
            NSWorkspace.shared.notificationCenter.removeObserver(obs)
            DistributedNotificationCenter.default().removeObserver(obs)
        }
    }

    private func setupObservers() {
        let wsCenter = NSWorkspace.shared.notificationCenter

        let sleepObs = wsCenter.addObserver(
            forName: NSWorkspace.willSleepNotification,
            object: nil,
            queue: nil
        ) { [weak self] _ in
            self?.hub.raiseSuspend(Date())
        }

        let wakeObs = wsCenter.addObserver(
            forName: NSWorkspace.didWakeNotification,
            object: nil,
            queue: nil
        ) { [weak self] _ in
            self?.hub.raiseResume(Date())
        }

        let powerOffObs = wsCenter.addObserver(
            forName: NSWorkspace.willPowerOffNotification,
            object: nil,
            queue: nil
        ) { [weak self] _ in
            self?.hub.raiseShutdown(Date())
        }

        observers.append(contentsOf: [sleepObs, wakeObs, powerOffObs])

        let distCenter = DistributedNotificationCenter.default()
        let lockObs = distCenter.addObserver(
            forName: NSNotification.Name("com.apple.screenIsLocked"),
            object: nil,
            queue: nil
        ) { [weak self] _ in
            self?.hub.raiseLock(Date())
        }

        let unlockObs = distCenter.addObserver(
            forName: NSNotification.Name("com.apple.screenIsUnlocked"),
            object: nil,
            queue: nil
        ) { [weak self] _ in
            self?.hub.raiseUnlock(Date())
        }

        observers.append(contentsOf: [lockObs, unlockObs])
    }
}
