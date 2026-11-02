import SwiftUI
import AppKit
import TimeSinkCore

public struct DevicesSyncView: View {
    @ObservedObject public var state: AppState

    @State private var syncNowState: String = "Sync now"
    @State private var isSyncing: Bool = false
    @State private var showingRenameSheet: Bool = false
    @State private var renamingDevice: DeviceRow?
    @State private var renameText: String = ""
    @State private var showingDeleteSheet: Bool = false
    @State private var deletingDevice: DeviceRow?
    @State private var deleteConfirmText: String = ""
    @State private var showingMergeSheet: Bool = false

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack(spacing: 6) {
                Button(action: {
                    state.showingDevicesSync = false
                }) {
                    HStack(spacing: 4) {
                        Text("‹").font(.system(size: 16))
                        Text("Settings").font(.system(size: 13))
                    }
                    .foregroundColor(ThemeColors.accent)
                    .padding(.horizontal, 8)
                    .frame(height: 28)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)

                Text("Devices and sync")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(maxWidth: .infinity)
                    .padding(.trailing, 76)
            }
            .padding(.horizontal, 10)
            .frame(height: 44)
            .bottomRule()

            // Scrollable Content
            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 14) {
                    // 1. Sync Folder Card
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(alignment: .center) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Sync folder")
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Works with OneDrive, Google Drive or any synced folder")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textMuted)
                            }

                            Spacer()

                            TSSwitch(isOn: Binding(
                                get: { state.syncEnabled },
                                set: { state.setSyncEnabled($0) }
                            ))
                        }

                        // Path Box
                        HStack(spacing: 8) {
                            IconView(.folder, size: 14, weight: 1.5, color: ThemeColors.textMuted)

                            Text(state.syncFolder.isEmpty ? "No sync folder selected" : (state.syncFolder as NSString).abbreviatingWithTildeInPath)
                                .font(.system(size: 11))
                                .foregroundColor(state.syncFolder.isEmpty ? ThemeColors.textMuted : ThemeColors.textDim)
                                .lineLimit(1)
                                .truncationMode(.tail)
                                .frame(maxWidth: .infinity, alignment: .leading)

                            Button(action: chooseSyncFolder) {
                                Text("Change…")
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 8)
                        .card(fill: Tokens.well, radius: 8)
                    }
                    .padding(12)
                    .card()

                    // 2. Devices Section
                    VStack(alignment: .leading, spacing: 6) {
                        Text("DEVICES")
                            .caps(10)
                            .padding(.horizontal, 4)

                        let devices = state.readDevices()
                        let activeRemoteCount = devices.filter { !$0.isLocal && $0.state == "active" }.count

                        VStack(spacing: 0) {
                            ForEach(Array(devices.enumerated()), id: \.element.id) { index, dev in
                                deviceRowView(dev: dev, index: index, activeRemoteCount: activeRemoteCount)

                                if index < devices.count - 1 {
                                    Rectangle().fill(ThemeColors.borderSubtle).frame(height: 1)
                                }
                            }
                        }
                        .card()
                    }

                    // Manage Synced Apps Card
                    let allDevices = state.readDevices()
                    let hasRemote = allDevices.contains { !$0.isLocal && $0.state == "active" }
                    let hasSyncedApps = (try? MacAppLibrary(db: state.db).getTrackedApps().contains { !$0.deviceId.isEmpty && !state.isLocalDevice($0.deviceId) || $0.mergedDeviceIds.contains { !$0.isEmpty && !state.isLocalDevice($0) } }) ?? false
                    if state.syncEnabled || hasRemote || hasSyncedApps {
                        Button(action: {
                            state.showingSettings = false
                            state.showingDevicesSync = true
                            state.returnToDevicesSyncOnDismiss = true
                            state.addAppInitialTab = .tracked
                            state.addAppInitialDeviceFilter = "synced"
                            state.showingAddApp = true
                        }) {
                            HStack(spacing: 10) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("Manage synced apps")
                                        .font(.system(size: 13, weight: .semibold))
                                        .foregroundColor(ThemeColors.textPrimary)
                                    Text("View and configure apps tracked across your devices")
                                        .font(.system(size: 11))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                                Spacer()
                                Text("›")
                                    .font(.system(size: 18, weight: .regular))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            .padding(.horizontal, 14)
                            .padding(.vertical, 12)
                            .card()
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }

                    // 3. Cross-device Merge Hint
                    let matchingApps = state.findMatchingCrossDeviceApps()
                    if !matchingApps.isEmpty {
                        HStack(spacing: 10) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(matchingApps.count) app\(matchingApps.count == 1 ? "" : "s") exist\(matchingApps.count == 1 ? "s" : "") on both devices")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.warning)

                                Text("Merge \(listText(matchingApps)) so they count as one app.")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)

                            Button(action: {
                                showingMergeSheet = true
                            }) {
                                Text("Merge…")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.background)
                                    .padding(.horizontal, 12)
                                    .frame(height: 28)
                                    .background(ThemeColors.warning)
                                    .cornerRadius(7)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 10)
                        .card(fill: Tokens.warnBg, border: Tokens.warnBorder)
                    }

                    // 4. Footnote
                    Text("Each device writes only its own file in the folder and reads the others, so nothing can overwrite anything. Only app names and session times are shared. Never window titles.")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                        .lineSpacing(3)
                        .padding(.horizontal, 2)
                }
                .padding(.horizontal, 14)
                .padding(.top, 14)
                .padding(.bottom, 10)
            }

            // Footer
            HStack {
                Text("Last sync · \(timeAgo(state.lastSyncUtc))")
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textMuted)

                Spacer()

                Button(action: handleSyncNow) {
                    Text(syncNowState)
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 14)
                        .frame(height: 28)
                        .background(ThemeColors.accent)
                        .cornerRadius(7)
                }
                .buttonStyle(.plain)
                .disabled(isSyncing)
            }
            .padding(.horizontal, 14)
            .frame(height: 44)
            .topRule()
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .sheet(isPresented: $showingRenameSheet) {
            renameSheetView
        }
        .sheet(isPresented: $showingDeleteSheet) {
            deleteSheetView
        }
        .sheet(isPresented: $showingMergeSheet) {
            mergeSheetView
        }
    }

    private func listText(_ names: [String]) -> String {
        let shown = Array(names.prefix(3))
        guard shown.count > 1 else { return shown.first ?? "" }
        return shown.dropLast().joined(separator: ", ") + " and " + shown.last!
    }

    private func platformLine(_ dev: DeviceRow) -> String {
        if dev.isLocal {
            let v = ProcessInfo.processInfo.operatingSystemVersion
            let running = state.todayResult?.runningCount ?? 0
            return "macOS \(v.majorVersion) · \(running) app\(running == 1 ? "" : "s") running"
        }
        return dev.platform == "macos" ? "macOS" : "Windows"
    }

    private func statusLine(_ dev: DeviceRow) -> String {
        if dev.isLocal {
            return state.lastSyncUtc > 0 ? "Wrote its file \(timeAgo(state.lastSyncUtc))" : "Ready to write file"
        }
        if dev.state == "forgotten" { return "Sync paused · forgotten" }
        if let imp = dev.lastImportUtc {
            return "Read its file \(timeAgo(imp)) · \(Formatters.grouped(dev.totalHours))h"
        }
        return state.syncFolder.contains("OneDrive") ? "Waiting for OneDrive to download its file" : "Waiting for its file to download"
    }

    @ViewBuilder
    private func deviceRowView(dev: DeviceRow, index: Int, activeRemoteCount: Int) -> some View {
        let status = deviceStatus(dev: dev, index: index, activeRemoteCount: activeRemoteCount)
        HStack(spacing: 12) {
            Text(dev.platform == "macos" ? "Mac" : "Win")
                .font(.system(size: 12, weight: .bold))
                .foregroundColor(dev.platform == "macos" ? ThemeColors.deviceA : Tokens.winBlue)
                .frame(width: 34, height: 34)
                .card(fill: ThemeColors.barBackground, border: ThemeColors.border, radius: 9)

            VStack(alignment: .leading, spacing: 0) {
                Text(dev.name + (dev.isLocal ? " · this Mac" : ""))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                Text(platformLine(dev))
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .padding(.top, 2)
                Text(statusLine(dev))
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            HStack(spacing: 5) {
                Circle().fill(status.color).frame(width: 6, height: 6)
                Text(status.text)
                    .font(.system(size: 11))
                    .foregroundColor(status.color)
                    .lineLimit(1)
            }
        }
        .padding(12)
        .contentShape(Rectangle())
        .contextMenu {
            Button("Rename…") {
                renamingDevice = dev
                renameText = dev.name
                showingRenameSheet = true
            }
            if !dev.isLocal {
                if dev.state == "active" {
                    Button("Forget") { state.forgetDevice(deviceId: dev.deviceId) }
                }
                Button("Delete history…", role: .destructive) {
                    deletingDevice = dev
                    deleteConfirmText = ""
                    showingDeleteSheet = true
                }
            }
        }
    }

    private struct StatusInfo {
        let text: String
        let color: Color
    }

    private func deviceStatus(dev: DeviceRow, index: Int, activeRemoteCount: Int) -> StatusInfo {
        if dev.state == "forgotten" {
            return StatusInfo(text: "Forgotten", color: ThemeColors.textMuted)
        } else if dev.isLocal {
            return StatusInfo(text: "Up to date", color: ThemeColors.deviceA)
        } else if activeRemoteCount > 1 && index >= 2 {
            return StatusInfo(text: "Only two devices are supported yet", color: ThemeColors.warning)
        } else if state.syncState == "no_other_device" {
            return StatusInfo(text: "No other device found", color: ThemeColors.warning)
        } else if state.syncState == "pending" {
            return StatusInfo(text: "Pending", color: ThemeColors.warning)
        } else {
            return StatusInfo(text: "Synced", color: ThemeColors.deviceA)
        }
    }

    private func chooseSyncFolder() {
        let panel = NSOpenPanel()
        panel.title = "Choose Sync Folder"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = true
        if panel.runModal() == .OK, let url = panel.url {
            state.setSyncFolder(url.path)
        }
    }

    private func handleSyncNow() {
        isSyncing = true
        syncNowState = "Syncing..."
        state.triggerSyncNow()
        Task { @MainActor in
            while state.isSyncing {
                try? await Task.sleep(nanoseconds: 100_000_000)
            }
            syncNowState = "Synced ✓"
            try? await Task.sleep(nanoseconds: 1_200_000_000)
            syncNowState = "Sync now"
            isSyncing = false
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

    // Rename Sheet
    private var renameSheetView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Rename Device")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)

            Text("Enter a new display name for this device:")
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textMuted)

            TextField("Device name", text: $renameText)
                .textFieldStyle(.roundedBorder)

            HStack {
                Spacer()
                Button("Cancel") {
                    showingRenameSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Button("Save") {
                    if let d = renamingDevice {
                        state.renameDevice(deviceId: d.deviceId, newName: renameText)
                    }
                    showingRenameSheet = false
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.accent)
            }
        }
        .padding(20)
        .frame(width: 320)
        .background(ThemeColors.surface)
    }

    // Delete Device History Sheet
    private var deleteSheetView: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Delete Device History")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.danger)

            if let d = deletingDevice {
                Text("This permanently deletes all usage history and tracked apps recorded for “\(d.name)”.\n\nType DELETE to confirm:")
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textDim)
            }

            TextField("DELETE", text: $deleteConfirmText)
                .textFieldStyle(.roundedBorder)

            HStack {
                Spacer()
                Button("Cancel") {
                    showingDeleteSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Button("Delete") {
                    if let d = deletingDevice, deleteConfirmText.trimmingCharacters(in: .whitespaces) == "DELETE" {
                        state.deleteDeviceHistory(deviceId: d.deviceId)
                    }
                    showingDeleteSheet = false
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.danger)
                .disabled(deleteConfirmText.trimmingCharacters(in: .whitespaces) != "DELETE")
            }
        }
        .padding(20)
        .frame(width: 340)
        .background(ThemeColors.surface)
    }
    // Merge Cross-Device Apps Sheet
    private var mergeSheetView: some View {
        let matchingApps = state.findMatchingCrossDeviceApps()
        let count = matchingApps.count
        let appList = listText(matchingApps)
        return VStack(alignment: .leading, spacing: 14) {
            Text("Merge Cross-Device Apps")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(ThemeColors.warning)

            Text("Merge \(count) \(count == 1 ? "app" : "apps") (\(appList)) across devices so their time is combined into single tracked apps?\n\nRemote instances will be merged under this Mac’s apps.")
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)
                .fixedSize(horizontal: false, vertical: true)

            HStack {
                Spacer()
                Button("Cancel") {
                    showingMergeSheet = false
                }
                .buttonStyle(.plain)
                .foregroundColor(ThemeColors.textMuted)

                Button("Merge") {
                    state.mergeMatchingCrossDeviceApps()
                    showingMergeSheet = false
                }
                .buttonStyle(.borderedProminent)
                .tint(ThemeColors.warning)
            }
        }
        .padding(20)
        .frame(width: 360)
        .background(ThemeColors.surface)
    }
}