import AppKit
import SwiftUI
import TimeSinkCore

@MainActor
public final class StatusBarController: NSObject {
    private let statusItem: NSStatusItem
    private let popover: NSPopover
    private var eventMonitor: Any?

    public init(popover: NSPopover) {
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        self.popover = popover
        super.init()

        if let button = statusItem.button {
            button.target = self
            button.action = #selector(togglePopover)
            button.title = "TimeSink"
        }
    }

    public func updateTitle(_ text: String) {
        if let button = statusItem.button {
            button.title = text
        }
    }

    public func updateTodayTotal(seconds: Int64) {
        let hours = seconds / 3600
        let minutes = (seconds % 3600) / 60
        let text = hours > 0 ? "\(hours)h \(minutes)m" : "\(minutes)m"
        updateTitle(text)
    }

    @objc private func togglePopover() {
        if popover.isShown {
            closePopover()
        } else {
            showPopover()
        }
    }

    private func showPopover() {
        guard let button = statusItem.button else { return }
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        eventMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            self?.closePopover()
        }
    }

    private func closePopover() {
        popover.performClose(nil)
        if let monitor = eventMonitor {
            NSEvent.removeMonitor(monitor)
            eventMonitor = nil
        }
    }
}

