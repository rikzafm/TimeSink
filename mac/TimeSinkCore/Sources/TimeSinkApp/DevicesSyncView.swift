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

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack(spacing: 8) {
                Button(action: {
                    state.showingDevicesSync = false
                }) {
                    HStack(spacing: 4) {
                        Text("‹")
                            .font(.system(size: 16, weight: .bold))
                        Text("Settings")
                            .font(.system(size: 13, weight: .medium))
                    }
                    .foregroundColor(ThemeColors.accent)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                }
                .buttonStyle(.plain)

                Spacer()

                Text("Devices and sync")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .offset(x: -30)

                Spacer()
            }
            .frame(height: 44)
            .padding(.horizontal, 10)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.border),
                alignment: .bottom
            )

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

                            Toggle("", isOn: Binding(
                                get: { state.syncEnabled },
                                set: { state.setSyncEnabled($0) }
                            ))
                            .toggleStyle(SwitchToggleStyle(tint: ThemeColors.accent))
                            .labelsHidden()
                        }

                        // Path Box
                        HStack(spacing: 8) {
                            Image(systemName: "folder")
                                .font(.system(size: 12))
                                .foregroundColor(ThemeColors.textMuted)

                            Text(state.syncFolder.isEmpty ? "No sync folder selected" : state.syncFolder)
                                .font(.system(size: 11))
                                .foregroundColor(state.syncFolder.isEmpty ? ThemeColors.textMuted : ThemeColors.textDim)
                                .lineLimit(1)
                                .truncationMode(.middle)

                            Spacer()

                            Button(action: chooseSyncFolder) {
                                Text("Change…")
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 8)
                        .background(ThemeColors.background)
                        .cornerRadius(8)
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(ThemeColors.border, lineWidth: 1)
                        )
                    }
                    .padding(12)
                    .background(ThemeColors.surface)
                    .cornerRadius(10)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )

                    // 2. Devices Section
                    VStack(alignment: .leading, spacing: 6) {
                        Text("DEVICES")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textMuted)
                            .padding(.horizontal, 4)

                        let devices = state.readDevices()
                        let activeRemoteCount = devices.filter { !$0.isLocal && $0.state == "active" }.count

                        VStack(spacing: 0) {
                            ForEach(Array(devices.enumerated()), id: \.element.id) { index, dev in
                                deviceRowView(dev: dev, index: index, activeRemoteCount: activeRemoteCount)

                                if index < devices.count - 1 {
                                    Divider()
                                        .background(ThemeColors.borderSubtle)
                                }
                            }
                        }
                        .background(ThemeColors.surface)
                        .cornerRadius(10)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .stroke(ThemeColors.border, lineWidth: 1)
                        )
                    }

                    // 3. Cross-device Merge Hint
                    let matchingApps = state.findMatchingCrossDeviceApps()
                    if !matchingApps.isEmpty {
                        HStack(spacing: 10) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("\(matchingApps.count) apps exist on both devices")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.warning)

                                Text("Merge \(matchingApps.prefix(3).joined(separator: ", ")) so they count as one app.")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            Button(action: {
                                state.showingDevicesSync = false
                                state.showingSettings = false
                                state.showingAddApp = true
                            }) {
                                Text("Merge…")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.background)
                                    .padding(.horizontal, 12)
                                    .padding(.vertical, 6)
                                    .background(ThemeColors.warning)
                                    .cornerRadius(7)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 10)
                        .background(ThemeColors.surface)
                        .cornerRadius(10)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .stroke(ThemeColors.warning.opacity(0.6), lineWidth: 1)
                        )
                    }

                    // 4. Footnote
                    Text("Each device writes only its own file in the folder and reads the others, so nothing can overwrite anything. Only app names and session times are shared. Never window titles.")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                        .lineSpacing(3)
                        .padding(.horizontal, 2)
                }
                .padding(14)
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
                        .padding(.vertical, 6)
                        .background(ThemeColors.accent)
                        .cornerRadius(7)
                }
                .buttonStyle(.plain)
                .disabled(isSyncing)
            }
            .frame(height: 44)
            .padding(.horizontal, 14)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.border),
                alignment: .top
            )
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .sheet(isPresented: $showingRenameSheet) {
            renameSheetView
        }
        .sheet(isPresented: $showingDeleteSheet) {
            deleteSheetView
        }
    }

    @ViewBuilder
    private func deviceRowView(dev: DeviceRow, index: Int, activeRemoteCount: Int) -> some View {
        VStack(spacing: 6) {
            HStack(spacing: 12) {
                // Platform Badge
                ZStack {
                    RoundedRectangle(cornerRadius: 8)
                        .fill(ThemeColors.surfaceRaised)
                        .frame(width: 34, height: 34)
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(ThemeColors.border, lineWidth: 1)
                        )

                    Text(dev.platform == "macos" ? "Mac" : "Win")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(dev.isLocal ? ThemeColors.deviceA : ThemeColors.deviceB)
                }

                // Info Lines
                VStack(alignment: .leading, spacing: 2) {
                    Text(dev.name + (dev.isLocal ? " · this Mac" : ""))
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)

                    Text("\(dev.platform == "macos" ? "macOS" : "Windows") · \(dev.appCount) apps")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)

                    let line2Text: String = {
                        if dev.isLocal {
                            return state.lastSyncUtc > 0 ? "Wrote its file \(timeAgo(state.lastSyncUtc))" : "Ready to write file"
                        } else if dev.state == "forgotten" {
                            return "Sync paused · forgotten"
                        } else if let imp = dev.lastImportUtc {
                            return "Read its file \(timeAgo(imp)) · \(dev.totalHours)h"
                        } else if state.syncState == "pending" {
                            return "Waiting for OneDrive to download its file"
                        } else {
                            return "Waiting for file to download"
                        }
                    }()

                    Text(line2Text)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                }

                Spacer()

                // Status Indicator
                VStack(alignment: .trailing, spacing: 4) {
                    let status = deviceStatus(dev: dev, index: index, activeRemoteCount: activeRemoteCount)
                    HStack(spacing: 5) {
                        Circle()
                            .fill(status.color)
                            .frame(width: 6, height: 6)
                        Text(status.text)
                            .font(.system(size: 11))
                            .foregroundColor(status.color)
                    }

                    // Actions
                    HStack(spacing: 8) {
                        Button("Rename") {
                            renamingDevice = dev
                            renameText = dev.name
                            showingRenameSheet = true
                        }
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                        .buttonStyle(.plain)

                        if !dev.isLocal {
                            if dev.state == "active" {
                                Button("Forget") {
                                    state.forgetDevice(deviceId: dev.deviceId)
                                }
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textMuted)
                                .buttonStyle(.plain)
                            }

                            Button("Delete history…") {
                                deletingDevice = dev
                                deleteConfirmText = ""
                                showingDeleteSheet = true
                            }
                            .font(.system(size: 11, weight: .medium))
                            .foregroundColor(ThemeColors.danger)
                            .buttonStyle(.plain)
                        }
                    }
                    .padding(.top, 2)
                }
            }
        }
        .padding(12)
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
        Task { @MainActor in
            state.triggerSyncNow()
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
}
