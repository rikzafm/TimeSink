import AppKit
import SwiftUI
import TimeSinkCore

@MainActor
public final class StatusBarController: NSObject {
    private let statusItem: NSStatusItem
    private let popover: NSPopover
    private var eventMonitor: Any?
    private var currentIconId: String

    public init(popover: NSPopover, initialIconId: String = IconCatalogue.defaultId) {
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        self.popover = popover
        self.currentIconId = initialIconId
        super.init()

        if let button = statusItem.button {
            button.target = self
            button.action = #selector(togglePopover)
            button.image = Self.iconImage(for: initialIconId)
            button.imagePosition = .imageOnly
        }
    }

    /// The icon image for the menu bar.
    public static func iconImage(for iconId: String) -> NSImage {
        let accentColor = NSColor(ThemeColors.accent)
        if iconId == "clean.clock" {
            return IconCatalogue.drawClockImage(size: 12, accentColor: accentColor)
        }
        if let img = IconCatalogue.image(for: iconId, size: 16, accentColor: accentColor) {
            return img
        }
        return IconCatalogue.drawClockImage(size: 12, accentColor: accentColor)
    }

    public func update(todaySeconds: Int64, showTime: Bool, iconId: String? = nil) {
        guard let button = statusItem.button else { return }
        if let iconId {
            self.currentIconId = iconId
        }
        button.image = Self.iconImage(for: currentIconId)
        if showTime && todaySeconds > 0 {
            button.imagePosition = .imageLeading
            button.title = " \(Formatters.paddedHM(todaySeconds))"
            button.font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        } else {
            button.imagePosition = .imageOnly
            button.title = ""
        }
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
