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
    private enum PassphraseField {
        case primary
        case confirm
    }
    @FocusState private var focusedPassphraseField: PassphraseField?
    @State private var passphraseInput: String = ""
    @State private var confirmPassphraseInput: String = ""
    @State private var isPassphraseRevealed: Bool = false
    @State private var isConfirmRevealed: Bool = false
    @State private var passphraseFeedback: String?
    @State private var isFeedbackDanger: Bool = false
    @State private var shakeAttempts: Int = 0

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

                    // Diagnostic Banner for Encryption Error States
                    if state.syncState == "encryption_key_wrong" ||
                       state.syncState == "encryption_key_missing" ||
                       state.syncState == "remote_not_encrypted" {
                        let isMismatch = state.syncState == "encryption_key_wrong"
                        let isMissing = state.syncState == "encryption_key_missing"
                        let bannerTitle = isMismatch ? "Sync Paused — Passphrase Mismatch"
                            : isMissing ? "Sync Paused — Passphrase Required"
                            : "Sync Alert — Remote Device Not Encrypted"
                        let bannerDesc = isMismatch ? "The entered passphrase does not match the other device. Make sure both devices use the exact same passphrase to decrypt sync archives."
                            : isMissing ? "The remote sync file is encrypted, but no passphrase is configured on this device. Enter your sync passphrase below to resume synchronization."
                            : "This device expects encrypted sync files, but the remote archive is unencrypted. Please configure the same passphrase on the other device."
                        let alertColor = isMismatch ? ThemeColors.danger : ThemeColors.warning

                        HStack(alignment: .top, spacing: 10) {
                            Image(systemName: isMismatch ? "exclamationmark.triangle.fill" : "lock.fill")
                                .font(.system(size: 13))
                                .foregroundColor(alertColor)
                                .padding(.top, 2)

                            VStack(alignment: .leading, spacing: 2) {
                                Text(bannerTitle)
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(alertColor)

                                Text(bannerDesc)
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                    .lineSpacing(2)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .padding(12)
                        .card(fill: Tokens.warnBg, border: alertColor)
                    }

                    // Diagnostic Banner for export failure
                    if state.syncState == SyncStates.exportFailed {
                        HStack(alignment: .top, spacing: 10) {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .font(.system(size: 13))
                                .foregroundColor(ThemeColors.danger)
                                .padding(.top, 2)

                            VStack(alignment: .leading, spacing: 2) {
                                Text("Couldn't write sync file")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.danger)

                                Text("Other devices won't see new activity from this Mac until this is fixed. TimeSink will keep retrying."
                                     + (state.syncLastError.isEmpty ? "" : " (\(state.syncLastError))"))
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                    .lineSpacing(2)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)

                            Button(action: handleSyncNow) {
                                Text("Retry")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.background)
                                    .padding(.horizontal, 12)
                                    .frame(height: 28)
                                    .background(ThemeColors.danger)
                                    .cornerRadius(7)
                            }
                            .buttonStyle(.plain)
                            .disabled(isSyncing)
                        }
                        .padding(12)
                        .card(fill: Tokens.warnBg, border: ThemeColors.danger)
                    }

                    // 1.5. Sync Encryption Card
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(alignment: .center) {
                            HStack(spacing: 6) {
                                Image(systemName: state.syncEncrypted && state.hasSyncPassphrase ? "lock.fill" : "lock")
                                    .font(.system(size: 12))
                                    .foregroundColor(state.syncEncrypted && state.hasSyncPassphrase ? ThemeColors.accent : ThemeColors.textMuted)

                                Text("Sync encryption")
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            Spacer()

                            // Status Badge
                            HStack(spacing: 5) {
                                Circle()
                                    .fill(state.syncEncrypted && state.hasSyncPassphrase ? ThemeColors.accent : ThemeColors.textMuted)
                                    .frame(width: 6, height: 6)

                                Text(state.syncEncrypted && state.hasSyncPassphrase ? "Encrypted" : "Not configured")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(state.syncEncrypted && state.hasSyncPassphrase ? ThemeColors.accent : ThemeColors.textMuted)
                            }
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
                            .card(fill: Tokens.well, radius: 6)
                        }

                        Text("Enter the same passphrase on both devices to encrypt your sync data. Your passphrase is stored securely in macOS Keychain and never sent over the network.")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                            .lineSpacing(2)

                        // 1. Passphrase Input
                        HStack(spacing: 0) {
                            if isPassphraseRevealed {
                                TextField("Enter sync passphrase…", text: $passphraseInput)
                                    .textFieldStyle(.plain)
                                    .font(.system(size: 12))
                                    .foregroundColor(ThemeColors.textPrimary)
                                    .focused($focusedPassphraseField, equals: .primary)
                                    .onSubmit {
                                        focusedPassphraseField = .confirm
                                    }
                            } else {
                                SecureField("Enter sync passphrase…", text: $passphraseInput)
                                    .textFieldStyle(.plain)
                                    .font(.system(size: 12))
                                    .foregroundColor(ThemeColors.textPrimary)
                                    .focused($focusedPassphraseField, equals: .primary)
                                    .onSubmit {
                                        focusedPassphraseField = .confirm
                                    }
                            }

                            Button(action: {
                                let savedRange = (NSApp.keyWindow?.firstResponder as? NSTextView)?.selectedRange() ?? NSRange(location: passphraseInput.count, length: 0)
                                isPassphraseRevealed.toggle()
                                focusedPassphraseField = .primary
                                DispatchQueue.main.async {
                                    if let textView = NSApp.keyWindow?.firstResponder as? NSTextView {
                                        textView.setSelectedRange(savedRange)
                                    }
                                }
                            }) {
                                IconView(isPassphraseRevealed ? .eyeSlash : .eye, size: 14, weight: 1.4, color: isPassphraseRevealed ? ThemeColors.accent : ThemeColors.textMuted)
                                    .padding(.horizontal, 6)
                                    .frame(maxHeight: .infinity)
                                    .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.leading, 10)
                        .frame(height: 28)
                        .background(Tokens.well)
                        .cornerRadius(7)
                        .overlay(
                            RoundedRectangle(cornerRadius: 7)
                                .stroke(Tokens.fieldBorder, lineWidth: 1)
                        )

                        // 2. Confirm Passphrase Input
                        HStack(spacing: 0) {
                            if isConfirmRevealed {
                                TextField("Confirm sync passphrase…", text: $confirmPassphraseInput)
                                    .textFieldStyle(.plain)
                                    .font(.system(size: 12))
                                    .foregroundColor(ThemeColors.textPrimary)
                                    .focused($focusedPassphraseField, equals: .confirm)
                                    .onSubmit {
                                        submitPassphrase()
                                    }
                            } else {
                                SecureField("Confirm sync passphrase…", text: $confirmPassphraseInput)
                                    .textFieldStyle(.plain)
                                    .font(.system(size: 12))
                                    .foregroundColor(ThemeColors.textPrimary)
                                    .focused($focusedPassphraseField, equals: .confirm)
                                    .onSubmit {
                                        submitPassphrase()
                                    }
                            }

                            Button(action: {
                                let savedRange = (NSApp.keyWindow?.firstResponder as? NSTextView)?.selectedRange() ?? NSRange(location: confirmPassphraseInput.count, length: 0)
                                isConfirmRevealed.toggle()
                                focusedPassphraseField = .confirm
                                DispatchQueue.main.async {
                                    if let textView = NSApp.keyWindow?.firstResponder as? NSTextView {
                                        textView.setSelectedRange(savedRange)
                                    }
                                }
                            }) {
                                IconView(isConfirmRevealed ? .eyeSlash : .eye, size: 14, weight: 1.4, color: isConfirmRevealed ? ThemeColors.accent : ThemeColors.textMuted)
                                    .padding(.horizontal, 6)
                                    .frame(maxHeight: .infinity)
                                    .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.leading, 10)
                        .frame(height: 28)
                        .background(Tokens.well)
                        .cornerRadius(7)
                        .overlay(
                            RoundedRectangle(cornerRadius: 7)
                                .stroke(confirmHasError ? ThemeColors.danger : Tokens.fieldBorder, lineWidth: 1)
                        )
                        .modifier(ShakeEffect(animatableData: CGFloat(shakeAttempts)))

                        if let feedback = passphraseFeedback {
                            Text(feedback)
                                .font(.system(size: 10))
                                .foregroundColor(isFeedbackDanger ? ThemeColors.danger : (feedback == "Passphrase saved" || feedback == "Passphrase cleared" ? ThemeColors.accent : ThemeColors.warning))
                                .padding(.horizontal, 2)
                        }

                        // Action Row
                        HStack(spacing: 8) {
                            Button(action: {
                                submitPassphrase()
                            }) {
                                Text(state.hasSyncPassphrase ? "Update" : "Set Passphrase")
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundColor(ThemeColors.background)
                                    .padding(.horizontal, 12)
                                    .frame(height: 28)
                                    .background(isPassphraseValid ? ThemeColors.accent : ThemeColors.textDisabled)
                                    .cornerRadius(7)
                            }
                            .buttonStyle(.plain)
                            .disabled(!isPassphraseValid)

                            if state.hasSyncPassphrase {
                                Button(action: {
                                    do {
                                        try state.clearSyncPassphrase()
                                        passphraseInput = ""
                                        confirmPassphraseInput = ""
                                        passphraseFeedback = "Passphrase cleared"
                                        isFeedbackDanger = false
                                    } catch {
                                        passphraseFeedback = "Failed to clear: \(error.localizedDescription)"
                                        isFeedbackDanger = true
                                    }
                                }) {
                                    Text("Clear")
                                        .font(.system(size: 11, weight: .semibold))
                                        .foregroundColor(ThemeColors.textMuted)
                                        .padding(.horizontal, 8)
                                        .frame(height: 28)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    .padding(12)
                    .card()
                    .onChange(of: passphraseInput) { updatePassphraseValidation() }
                    .onChange(of: confirmPassphraseInput) { updatePassphraseValidation() }

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
            return state.lastExportUtc > 0 ? "Wrote its file \(timeAgo(state.lastExportUtc))" : "Ready to write file"
        }
        if dev.state == "forgotten" { return "Sync paused · forgotten" }
        if let file = dev.lastFileUtc {
            return "\(remoteFileText(file)) · \(Formatters.grouped(dev.totalHours))h"
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
                if !dev.isLocal && dev.state != "forgotten", let file = dev.lastFileUtc, let hint = staleHint(file) {
                    Text(hint)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                        .lineLimit(1)
                }
                if dev.isLocal && state.exportStale {
                    Text("Last wrote its file \(timeAgo(state.lastExportUtc))")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.warning)
                        .lineLimit(1)
                }
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
        } else if dev.isLocal && state.syncState == SyncStates.exportFailed {
            return StatusInfo(text: "Couldn't write file", color: ThemeColors.danger)
        } else if dev.isLocal {
            return StatusInfo(text: "Up to date", color: ThemeColors.deviceA)
        } else if activeRemoteCount > 1 && index >= 2 {
            return StatusInfo(text: "Only two devices are supported yet", color: ThemeColors.warning)
        } else if state.syncState == "encryption_key_wrong" {
            return StatusInfo(text: "Passphrase mismatch", color: ThemeColors.danger)
        } else if state.syncState == "encryption_key_missing" {
            return StatusInfo(text: "Passphrase required", color: ThemeColors.warning)
        } else if state.syncState == "remote_not_encrypted" {
            return StatusInfo(text: "Remote unencrypted", color: ThemeColors.warning)
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

    /// Mirrors C# RemoteFileText.Describe line (src/TimeSink.Core/Sync/RemoteFileText.cs).
    private func remoteFileText(_ fileUtc: Int64) -> String {
        guard fileUtc > 0 else { return "File not read yet" }
        let age = max(0, Int64(Date().timeIntervalSince1970) - fileUtc)
        if age < 60 { return "File from just now" }
        if age < 3600 { return "File from \(age / 60) min ago" }
        if age < 86400 { return "File from \(age / 3600) h ago" }
        let days = age / 86400
        return days == 1 ? "File from 1 day ago" : "File from \(days) days ago"
    }

    /// Neutral hint (not an error) when the other device's file is 24 h or older.
    private func staleHint(_ fileUtc: Int64) -> String? {
        guard fileUtc > 0, Int64(Date().timeIntervalSince1970) - fileUtc >= 86400 else { return nil }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "MMM d"
        return "Other device hasn't synced since " + f.string(from: Date(timeIntervalSince1970: TimeInterval(fileUtc)))
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

    private var isPassphraseValid: Bool {
        passphraseInput.count >= 8 && passphraseInput == confirmPassphraseInput
    }

    private var confirmHasError: Bool {
        shakeAttempts > 0 || (!confirmPassphraseInput.isEmpty && passphraseInput != confirmPassphraseInput)
    }

    private func updatePassphraseValidation() {
        if passphraseInput.isEmpty && confirmPassphraseInput.isEmpty {
            passphraseFeedback = nil
            isFeedbackDanger = false
            return
        }

        if passphraseInput.count < 8 {
            passphraseFeedback = "Passphrase must be at least 8 characters."
            isFeedbackDanger = false
            return
        }

        if !confirmPassphraseInput.isEmpty && passphraseInput != confirmPassphraseInput {
            passphraseFeedback = "Passphrases do not match."
            isFeedbackDanger = true
            return
        }

        if isPassphraseValid {
            passphraseFeedback = nil
            isFeedbackDanger = false
        }
    }

    private func submitPassphrase() {
        if isPassphraseValid {
            do {
                try state.setSyncPassphrase(passphraseInput)
                passphraseInput = ""
                confirmPassphraseInput = ""
                passphraseFeedback = "Passphrase saved"
                isFeedbackDanger = false
            } catch {
                passphraseFeedback = "Failed to save: \(error.localizedDescription)"
                isFeedbackDanger = true
            }
        } else {
            withAnimation(.default) {
                shakeAttempts += 1
            }
            if passphraseInput.count < 8 {
                passphraseFeedback = "Passphrase must be at least 8 characters."
                isFeedbackDanger = false
            } else if passphraseInput != confirmPassphraseInput {
                passphraseFeedback = "Passphrases do not match."
                isFeedbackDanger = true
            }
        }
    }
}

struct ShakeEffect: GeometryEffect {
    var amount: CGFloat = 6
    var shakesPerUnit: CGFloat = 3
    var animatableData: CGFloat

    func effectValue(size: CGSize) -> ProjectionTransform {
        ProjectionTransform(CGAffineTransform(translationX:
            amount * sin(animatableData * .pi * shakesPerUnit),
            y: 0))
    }
}
