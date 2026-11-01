import SwiftUI
import AppKit
import TimeSinkCore

public struct SettingsView: View {
    @ObservedObject public var state: AppState

    @State private var backupStatusText: String = "Run"
    @State private var isBackingUp: Bool = false
    @State private var showingRestoreSheet: Bool = false
    @State private var pendingRestoreFile: String = ""
    @State private var restoreInspection: BackupInspection?
    @State private var restoreStatusMessage: String?
    @State private var showingExportSheet: Bool = false
    @State private var showingDeleteAllSheet: Bool = false
    @State private var deleteAllConfirmText: String = ""
    @State private var showingRenameMacSheet: Bool = false
    @State private var renameMacText: String = ""

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        Group {
            if state.showingDevicesSync {
                DevicesSyncView(state: state)
            } else if state.showingAppIconPicker {
                AppIconPickerView(state: state)
            } else {
                mainSettingsView
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .sheet(isPresented: $showingRestoreSheet) {
            restoreSheetView
        }
        .sheet(isPresented: $showingExportSheet) {
            exportSheetView
        }
        .sheet(isPresented: $showingDeleteAllSheet) {
            deleteAllSheetView
        }
        .sheet(isPresented: $showingRenameMacSheet) {
            renameMacSheetView
        }
    }

    private var mainSettingsView: some View {
        VStack(spacing: 0) {
            // Header
            HStack(spacing: 6) {
                Button(action: {
                    state.showingSettings = false
                }) {
                    HStack(spacing: 4) {
                        Text("‹").font(.system(size: 16))
                        Text("Back").font(.system(size: 13))
                    }
                    .foregroundColor(ThemeColors.accent)
                    .padding(.horizontal, 8)
                    .frame(height: 28)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)

                Text("Settings")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(maxWidth: .infinity)
                    .padding(.trailing, 56)
            }
            .padding(.horizontal, 10)
            .frame(height: 44)
            .bottomRule()

            // Scrollable Settings Sections
            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 14) {
                    // GENERAL
                    settingsSection(title: "GENERAL") {
                        settingSwitchRow(
                            label: "Open at login",
                            desc: "Starts quietly in the menu bar",
                            isOn: Binding(
                                get: { state.openAtLogin },
                                set: { state.setOpenAtLogin($0) }
                            )
                        )
                        settingsRule
                        settingSegmentedRow(
                            label: "Week starts on",
                            desc: "",
                            options: [("Mon", "Mon"), ("Sun", "Sun"), ("Sat", "Sat")],
                            selection: Binding(
                                get: { WeekStart.parse(state.weekStart).rawValue },
                                set: { state.setWeekStart($0) }
                            )
                        )
                    }

                    // TRACKING
                    settingsSection(title: "TRACKING") {
                        settingSwitchRow(
                            label: "Pause while the Mac sleeps",
                            desc: "",
                            isOn: Binding(
                                get: { state.pauseWhileSleep },
                                set: { state.setPauseWhileSleep($0) }
                            )
                        )
                        settingsRule
                        settingSwitchRow(
                            label: "Keep counting when locked",
                            desc: "",
                            isOn: Binding(
                                get: { state.countWhenLocked },
                                set: { state.setCountWhenLocked($0) }
                            )
                        )
                        settingsRule
                        settingSegmentedRow(
                            label: "Ignore short sessions",
                            desc: "Shorter than this are dropped",
                            options: [(5, "5s"), (15, "15s"), (30, "30s")],
                            selection: Binding(
                                get: { state.minSessionSeconds },
                                set: { state.setMinSessionSeconds($0) }
                            )
                        )
                    }

                    // APPEARANCE
                    settingsSection(title: "APPEARANCE") {
                        settingSegmentedRow(
                            label: "Theme",
                            desc: "Follows macOS when set to System",
                            options: [("System", "System"), ("Dark", "Dark"), ("Light", "Light")],
                            selection: Binding(
                                get: { state.theme },
                                set: { state.setTheme($0) }
                            )
                        )
                        settingsRule
                        settingSegmentedRow(
                            label: "Accent",
                            desc: "",
                            options: [("Lime", "Lime"), ("Blue", "Blue"), ("Pink", "Pink")],
                            selection: Binding(
                                get: { state.accent },
                                set: { state.setAccent($0) }
                            )
                        )
                        settingsRule
                        settingSwitchRow(
                            label: "Show time in the menu bar",
                            desc: "Today’s total next to the icon",
                            isOn: Binding(
                                get: { state.showMenuBarTime },
                                set: { state.setShowMenuBarTime($0) }
                            )
                        )
                        settingsRule
                        Button(action: {
                            state.showingAppIconPicker = true
                        }) {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("App icon")
                                        .font(.system(size: 13))
                                        .foregroundColor(ThemeColors.textPrimary)
                                    Text("How TimeSink looks in the menu bar.")
                                        .font(.system(size: 11))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                                Spacer()
                                HStack(spacing: 6) {
                                    let currentId = state.appIcon
                                    if let entry = IconCatalogue.find(currentId) {
                                        if let thumb = IconCatalogue.image(for: currentId, size: 20, accentColor: NSColor(ThemeColors.accent)) {
                                            Image(nsImage: thumb)
                                                .resizable()
                                                .interpolation(.high)
                                                .aspectRatio(contentMode: .fit)
                                                .frame(width: 20, height: 20)
                                                .cornerRadius(4)
                                        }
                                        Text(entry.name)
                                            .font(.system(size: 12))
                                            .foregroundColor(ThemeColors.textDim)
                                    }
                                    Text("›")
                                        .font(.system(size: 12))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .frame(minHeight: 44)
                        }
                        .buttonStyle(.plain)
                    }

                    // DEVICES AND SYNC
                    settingsSection(title: "DEVICES AND SYNC") {
                        Button(action: {
                            state.showingDevicesSync = true
                        }) {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("Sync with other devices")
                                        .font(.system(size: 13))
                                        .foregroundColor(ThemeColors.textPrimary)
                                    let syncDesc: String = {
                                        if !state.syncEnabled { return "Off" }
                                        if state.syncFolder.isEmpty { return "No sync folder set" }
                                        let name = URL(fileURLWithPath: state.syncFolder).lastPathComponent
                                        let isOneDrive = state.syncFolder.contains("OneDrive")
                                        return isOneDrive ? "OneDrive · \(name) folder" : "\(name) folder"
                                    }()
                                    Text(syncDesc)
                                        .font(.system(size: 11))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                                Spacer()
                                HStack(spacing: 4) {
                                    Text(state.syncEnabled ? "On" : "Off")
                                        .font(.system(size: 12))
                                        .foregroundColor(ThemeColors.textDim)
                                    Text("›")
                                        .font(.system(size: 12))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .frame(minHeight: 44)
                        }
                        .buttonStyle(.plain)

                        settingsRule

                        Button(action: {
                            renameMacText = state.localDeviceName
                            showingRenameMacSheet = true
                        }) {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("This Mac is called")
                                        .font(.system(size: 13))
                                        .foregroundColor(ThemeColors.textPrimary)
                                    Text("Shown on combined reports")
                                        .font(.system(size: 11))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                                Spacer()
                                HStack(spacing: 4) {
                                    Text(state.localDeviceName)
                                        .font(.system(size: 12))
                                        .foregroundColor(ThemeColors.textDim)
                                    Text("›")
                                        .font(.system(size: 12))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .frame(minHeight: 44)
                        }
                        .buttonStyle(.plain)

                        settingsRule

                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Use short device names")
                                    .font(.system(size: 13))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Show short labels (PC / Mac) on views instead of full device names")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            Spacer()
                            Toggle("", isOn: Binding(
                                get: { state.shortDeviceNames },
                                set: { state.setShortDeviceNames($0) }
                            ))
                            .labelsHidden()
                            .toggleStyle(SwitchToggleStyle(tint: ThemeColors.accent))
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .frame(minHeight: 44)
                    }

                    // BACKUP AND DATA
                    settingsSection(title: "BACKUP AND DATA") {
                        settingSegmentedRow(
                            label: "Automatic backup",
                            desc: "Keeps the last 7 plus one per month",
                            options: [("Off", "Off"), ("Daily", "Daily"), ("Weekly", "Weekly")],
                            selection: Binding(
                                get: { state.backupMode },
                                set: { state.setBackupMode($0) }
                            )
                        )
                        settingsRule

                        // Back up now
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Back up now")
                                    .font(.system(size: 13))
                                    .foregroundColor(ThemeColors.textPrimary)
                                let backupDesc = state.lastBackupUtc > 0 ? "Last backup: \(timeAgo(state.lastBackupUtc))" : "No backup made yet"
                                Text(backupDesc)
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            Spacer()
                            Button(action: handleBackupNow) {
                                Text(backupStatusText)
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                            .disabled(isBackingUp)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .frame(minHeight: 44)

                        settingsRule

                        // Restore
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Restore from backup")
                                    .font(.system(size: 13))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Replace or merge data from a .timesink file")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            Spacer()
                            Button(action: chooseRestoreFile) {
                                Text("Choose file…")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .frame(minHeight: 44)

                        settingsRule

                        // Export data
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Export data")
                                    .font(.system(size: 13))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Export sessions and daily totals as CSV or JSON")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            Spacer()
                            Button(action: { showingExportSheet = true }) {
                                Text("Export…")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .frame(minHeight: 44)

                        settingsRule

                        // Delete all history
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Delete all history")
                                    .font(.system(size: 13))
                                    .foregroundColor(ThemeColors.danger)
                                Text("Apps and settings are kept")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            Spacer()
                            Button(action: {
                                deleteAllConfirmText = ""
                                showingDeleteAllSheet = true
                            }) {
                                Text("Delete…")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.danger)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .frame(minHeight: 44)
                    }

                    // ABOUT
                    settingsSection(title: "ABOUT") {
                        settingValueRow(
                            label: "TimeSink for Mac",
                            desc: "Native Apple Silicon · Version 1.0.0",
                            value: ""
                        )
                    }
                }
                .padding(.horizontal, 14)
                .padding(.top, 10)
                .padding(.bottom, 14)
            }
        }
    }

    private var settingsRule: some View {
        Rectangle().fill(ThemeColors.borderSubtle).frame(height: 1)
    }

    // MARK: - Row Helpers

    @ViewBuilder
    private func settingsSection<Content: View>(title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .caps(10)
                .padding(.horizontal, 4)

            VStack(spacing: 0) {
                content()
            }
            .card()
        }
    }

    private func rowLabels(_ label: String, _ desc: String) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(label)
                .font(.system(size: 13))
                .foregroundColor(ThemeColors.textPrimary)
            if !desc.isEmpty {
                Text(desc)
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
        }
    }

    @ViewBuilder
    private func settingSwitchRow(label: String, desc: String, isOn: Binding<Bool>) -> some View {
        HStack(spacing: 10) {
            rowLabels(label, desc)
            Spacer()
            TSSwitch(isOn: isOn)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .frame(minHeight: 44)
    }

    @ViewBuilder
    private func settingSegmentedRow<T: Hashable>(label: String, desc: String, options: [(T, String)], selection: Binding<T>) -> some View {
        HStack(spacing: 10) {
            rowLabels(label, desc)
            Spacer()
            Segmented(
                options: options,
                selection: selection,
                hPad: 8,
                radius: 7,
                fill: Tokens.well,
                border: ThemeColors.borderSubtle
            )
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .frame(minHeight: 44)
    }

    @ViewBuilder
    private func settingValueRow(label: String, desc: String, value: String) -> some View {
        HStack(spacing: 10) {
            rowLabels(label, desc)
            Spacer()
            if !value.isEmpty {
                (Text(value).foregroundColor(ThemeColors.textDim) + Text(" ›").foregroundColor(ThemeColors.textMuted))
                    .font(.system(size: 12))
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .frame(minHeight: 44)
    }

    // MARK: - Handlers

    private func handleBackupNow() {
        isBackingUp = true
        backupStatusText = "Backing up..."
        Task { @MainActor in
            let res = state.backupNow()
            if res.success {
                backupStatusText = "Done ✓"
            } else {
                backupStatusText = "Failed"
            }
            try? await Task.sleep(nanoseconds: 2_000_000_000)
            backupStatusText = "Run"
            isBackingUp = false
        }
    }

    private func chooseRestoreFile() {
        let panel = NSOpenPanel()
        panel.title = "Choose TimeSink Backup File"
        panel.allowedContentTypes = []
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url {
            let file = url.path
            do {
                let inspection = try BackupService.inspect(backupFile: file)
                self.pendingRestoreFile = file
                self.restoreInspection = inspection
                self.restoreStatusMessage = nil
                self.showingRestoreSheet = true
            } catch {
                let alert = NSAlert()
                alert.messageText = "Cannot Restore Backup"
                alert.informativeText = error.localizedDescription
                alert.runModal()
            }
        }
    }

    private func timeAgo(_ utcSeconds: Int64) -> String {
        guard utcSeconds > 0 else { return "never" }
        let span = Date().timeIntervalSince1970 - Double(utcSeconds)
        if span < 60 { return "just now" }
        let mins = Int(span / 60)
        if mins < 60 { return "\(mins) min ago" }
        let hours = Int(span / 3600)
        if hours < 24 { return "\(hours) h ago" }
        let days = Int(span / 86400)
        return "\(days) d ago"
    }

    // MARK: - Sheets

    // Restore Sheet
    private var restoreSheetView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Restore Backup")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)

            if let insp = restoreInspection {
                let fileName = URL(fileURLWithPath: pendingRestoreFile).lastPathComponent
                Text("Backup file: \(fileName)\nCreated: \(insp.manifest.createdUtc)\nContains: \(insp.apps) apps, \(insp.sessions) sessions" + (insp.needsMigration ? "\n(Requires schema migration)" : ""))
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textDim)
                    .lineSpacing(2)

                Text("Replace replaces the live database with this backup after making a safety copy. Merge adds apps and sessions from the backup into your existing history.")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineSpacing(2)
            }

            if let msg = restoreStatusMessage {
                Text(msg)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(ThemeColors.accent)
            }

            HStack {
                Button("Cancel") {
                    showingRestoreSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Spacer()

                Button("Merge") {
                    do {
                        let res = try state.restore(from: pendingRestoreFile, replace: false)
                        restoreStatusMessage = res
                        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                            showingRestoreSheet = false
                        }
                    } catch {
                        restoreStatusMessage = "Error: \(error.localizedDescription)"
                    }
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.accent)

                Button("Replace") {
                    do {
                        let res = try state.restore(from: pendingRestoreFile, replace: true)
                        restoreStatusMessage = res
                        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                            showingRestoreSheet = false
                        }
                    } catch {
                        restoreStatusMessage = "Error: \(error.localizedDescription)"
                    }
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.warning)
            }
        }
        .padding(20)
        .frame(width: 360)
        .background(ThemeColors.surface)
    }

    // Export Sheet
    private var exportSheetView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Export Data")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)

            Text("Export all tracked sessions and daily totals into CSV spreadsheets or a single JSON file.")
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)

            HStack {
                Button("Cancel") {
                    showingExportSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Spacer()

                Button("Export CSV…") {
                    showingExportSheet = false
                    let panel = NSOpenPanel()
                    panel.title = "Select Folder for CSV Export"
                    panel.canChooseFiles = false
                    panel.canChooseDirectories = true
                    panel.canCreateDirectories = true
                    if panel.runModal() == .OK, let url = panel.url {
                        _ = try? state.exportCsv(folder: url.path)
                    }
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.accent)

                Button("Export JSON…") {
                    showingExportSheet = false
                    let panel = NSSavePanel()
                    panel.title = "Export JSON File"
                    panel.nameFieldStringValue = "timesink-export.json"
                    if panel.runModal() == .OK, let url = panel.url {
                        try? state.exportJson(file: url.path)
                    }
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.accent)
            }
        }
        .padding(20)
        .frame(width: 340)
        .background(ThemeColors.surface)
    }

    // Delete All History Sheet
    private var deleteAllSheetView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Delete All History")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.danger)

            Text("This permanently deletes all session history, daily totals, and cached icons. Tracked app lists and settings are preserved.\n\nType DELETE to confirm:")
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)

            TextField("DELETE", text: $deleteAllConfirmText)
                .textFieldStyle(.roundedBorder)

            HStack {
                Spacer()
                Button("Cancel") {
                    showingDeleteAllSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Button("Delete") {
                    if deleteAllConfirmText.trimmingCharacters(in: .whitespaces) == "DELETE" {
                        try? state.deleteAllHistory(confirmation: "DELETE")
                        showingDeleteAllSheet = false
                    }
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.danger)
                .disabled(deleteAllConfirmText.trimmingCharacters(in: .whitespaces) != "DELETE")
            }
        }
        .padding(20)
        .frame(width: 340)
        .background(ThemeColors.surface)
    }

    // Rename Mac Sheet
    private var renameMacSheetView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Rename this Mac")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)

            Text("Enter a new display name for this Mac:")
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textMuted)

            TextField("Device name", text: $renameMacText)
                .textFieldStyle(.roundedBorder)

            HStack {
                Spacer()
                Button("Cancel") {
                    showingRenameMacSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Button("Save") {
                    state.renameDevice(deviceId: state.localDeviceId, newName: renameMacText)
                    showingRenameMacSheet = false
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.accent)
            }
        }
        .padding(20)
        .frame(width: 320)
        .background(ThemeColors.surface)
    }
}
