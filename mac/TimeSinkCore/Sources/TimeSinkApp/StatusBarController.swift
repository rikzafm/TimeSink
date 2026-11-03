import AppKit
import SwiftUI
import TimeSinkCore

@MainActor
public final class StatusBarController: NSObject, NSPopoverDelegate {
    private let statusItem: NSStatusItem
    private let popover: NSPopover
    private var eventMonitor: Any?
    private var keyEventMonitor: Any?
    private var currentIconId: String
    private weak var appState: AppState?

    public init(popover: NSPopover, appState: AppState? = nil, initialIconId: String = IconCatalogue.defaultId) {
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        self.popover = popover
        self.appState = appState
        self.currentIconId = initialIconId
        super.init()
        self.popover.delegate = self

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
        appState?.startLiveUpdates()
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        eventMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            self?.closePopover()
        }
        keyEventMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if self?.handleKeyEvent(event) == true {
                return nil
            }
            return event
        }
    }

    public func closePopover() {
        appState?.stopLiveUpdates()
        popover.performClose(nil)
        if let monitor = eventMonitor {
            NSEvent.removeMonitor(monitor)
            eventMonitor = nil
        }
        if let keyMon = keyEventMonitor {
            NSEvent.removeMonitor(keyMon)
            keyEventMonitor = nil
        }
    }

    public func popoverWillShow(_ notification: Notification) {
        appState?.startLiveUpdates()
    }

    public func popoverDidClose(_ notification: Notification) {
        appState?.stopLiveUpdates()
        if let monitor = eventMonitor {
            NSEvent.removeMonitor(monitor)
            eventMonitor = nil
        }
        if let keyMon = keyEventMonitor {
            NSEvent.removeMonitor(keyMon)
            keyEventMonitor = nil
        }
    }

    private func handleKeyEvent(_ event: NSEvent) -> Bool {
        guard let state = appState else { return false }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let isCmd = flags.contains(.command)
        let isCtrl = flags.contains(.control)
        let isShift = flags.contains(.shift)
        let isOption = flags.contains(.option)

        // 1. Direct tab switching: Cmd+1 .. Cmd+5
        if isCmd && !isOption && !isShift && !isCtrl {
            if let chars = event.charactersIgnoringModifiers, let num = Int(chars), num >= 1 && num <= 5 {
                let tabs = AppTab.allCases
                if num - 1 < tabs.count {
                    state.switchToTab(tabs[num - 1])
                    return true
                }
            }
        }

        // 2. Tab cycling: Ctrl+Tab / Ctrl+Shift+Tab or Cmd+Shift+[ / Cmd+Shift+]
        if isCtrl && !isCmd && !isOption && event.keyCode == 48 /* Tab */ {
            state.cycleTab(forward: !isShift)
            return true
        }
        if isCmd && isShift && !isCtrl && !isOption {
            if event.keyCode == 30 /* ] */ {
                state.cycleTab(forward: true)
                return true
            } else if event.keyCode == 33 /* [ */ {
                state.cycleTab(forward: false)
                return true
            }
        }

        // 3. Toggle Settings: Cmd+,
        if isCmd && !isOption && !isShift && !isCtrl && (event.charactersIgnoringModifiers == "," || event.keyCode == 43) {
            state.toggleSettings()
            return true
        }

        // 4. Add App: Cmd+N
        if isCmd && !isOption && !isShift && !isCtrl && (event.charactersIgnoringModifiers == "n" || event.keyCode == 45) {
            state.showAddApp()
            return true
        }

        // 5. Toggle Games: Cmd+G
        if isCmd && !isOption && !isShift && !isCtrl && (event.charactersIgnoringModifiers == "g" || event.keyCode == 5) {
            state.toggleGamesOnly()
            return true
        }

        // 6. Date navigation: Option+Left / Option+Right or Cmd+[ / Cmd+]
        if (isOption && !isCmd && !isShift && !isCtrl) || (isCmd && !isOption && !isShift && !isCtrl) {
            let isLeft = event.keyCode == 123 || (isCmd && event.keyCode == 33 /* [ */)
            let isRight = event.keyCode == 124 || (isCmd && event.keyCode == 30 /* ] */)
            if isLeft || isRight {
                if isLeft && state.isDrilldownOrOverlay {
                    state.handleGoBack()
                    return true
                }
                state.navigateDate(step: isLeft ? -1 : 1)
                return true
            }
        }

        // 7. Secondary Go Back: Delete (Backspace) outside a focused text field
        if !isCmd && !isCtrl && !isOption && !isShift && event.keyCode == 51 /* Delete/Backspace */ {
            let firstResponder = NSApp.keyWindow?.firstResponder
            if !(firstResponder is NSTextView || firstResponder is NSTextField) {
                if state.isDrilldownOrOverlay {
                    state.handleGoBack()
                    return true
                }
            }
        }

        // 8. Shortcuts guide: '?' (Shift+/) outside a focused text field
        if (event.characters == "?" || (event.charactersIgnoringModifiers == "/" && isShift)) && !isCmd && !isCtrl && !isOption {
            let firstResponder = NSApp.keyWindow?.firstResponder
            if !(firstResponder is NSTextView || firstResponder is NSTextField) {
                state.toggleKeyboardGuide()
                return true
            }
        }

        // 9. ESC navigation
        if event.keyCode == 53 /* Escape */ {
            state.handleEscape()
            return true
        }

        return false
    }
}
