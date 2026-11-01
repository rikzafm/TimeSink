import AppKit
import SwiftUI
import TimeSinkCore

@MainActor
public final class StatusBarController: NSObject {
    private let statusItem: NSStatusItem
    private let popover: NSPopover
    private var eventMonitor: Any?
    private var lastText = ""
    public var showsTime = true {
        didSet { applyTitle() }
    }

    public init(popover: NSPopover) {
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        self.popover = popover
        super.init()

        if let button = statusItem.button {
            button.target = self
            button.action = #selector(togglePopover)
            button.image = Self.clockImage()
            button.imagePosition = .imageLeading
        }
    }

    public func updateTitle(_ text: String) {
        lastText = text
        applyTitle()
    }

    private func applyTitle() {
        statusItem.button?.title = showsTime ? lastText : ""
    }

    /// The board's lime clock glyph.
    private static func clockImage() -> NSImage {
        let img = NSImage(size: NSSize(width: 12, height: 12), flipped: true) { _ in
            let lime = NSColor(red: 0.776, green: 0.949, blue: 0.420, alpha: 1)
            lime.setStroke()
            let ring = NSBezierPath(ovalIn: NSRect(x: 1.5, y: 1.5, width: 9, height: 9))
            ring.lineWidth = 1.6
            ring.stroke()
            let hand = NSBezierPath()
            hand.move(to: NSPoint(x: 6, y: 3.5))
            hand.line(to: NSPoint(x: 6, y: 6))
            hand.line(to: NSPoint(x: 7.8, y: 7))
            hand.lineWidth = 1.4
            hand.lineCapStyle = .round
            hand.stroke()
            return true
        }
        img.isTemplate = false
        return img
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

