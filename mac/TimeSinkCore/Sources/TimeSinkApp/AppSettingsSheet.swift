import SwiftUI
import TimeSinkCore

public struct AppSettingsSheet: View {
    @ObservedObject var state: AppState
    public let appId: Int64
    public let onDismiss: () -> Void

    @State private var displayName: String = ""
    @State private var kind: AppKind = .other
    @State private var isBackground: Bool = false
    @State private var mergedChildren: [TrackedAppItem] = []
    @State private var showingMerge: Bool = false
    @State private var showingStopConfirm: Bool = false
    @State private var showingClearConfirm: Bool = false

    public init(state: AppState, appId: Int64, onDismiss: @escaping () -> Void) {
        self.state = state
        self.appId = appId
        self.onDismiss = onDismiss
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Button(action: onDismiss) {
                    HStack(spacing: 4) {
                        Image(systemName: "chevron.left")
                            .font(.system(size: 11, weight: .bold))
                        Text("Back")
                            .font(.system(size: 13, weight: .medium))
                    }
                    .foregroundColor(ThemeColors.accent)
                }
                .buttonStyle(.plain)

                Spacer()

                Text("App Settings")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.borderSubtle),
                alignment: .bottom
            )

            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    // App Name Section
                    VStack(alignment: .leading, spacing: 6) {
                        Text("DISPLAY NAME")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)

                        TextField("App name", text: $displayName)
                            .textFieldStyle(.plain)
                            .font(.system(size: 13))
                            .foregroundColor(ThemeColors.textPrimary)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 7)
                            .background(ThemeColors.surface)
                            .cornerRadius(6)
                            .overlay(
                                RoundedRectangle(cornerRadius: 6)
                                    .stroke(ThemeColors.border, lineWidth: 1)
                            )
                    }

                    // App Kind (App, Game, Gacha)
                    VStack(alignment: .leading, spacing: 6) {
                        Text("APP KIND")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)

                        HStack(spacing: 8) {
                            kindButton(title: "App", val: .other)
                            kindButton(title: "Game", val: .game)
                            kindButton(title: "Gacha", val: .gacha)
                        }
                    }

                    // Background App Toggle
                    VStack(alignment: .leading, spacing: 6) {
                        Toggle(isOn: $isBackground) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Background App")
                                    .font(.system(size: 13, weight: .medium))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Counts only when explicitly focused")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                        }
                        .toggleStyle(.switch)
                    }

                    // Merged Apps Section
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("MERGED APPS")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            Button(action: {
                                showingMerge = true
                            }) {
                                Text("+ Merge…")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                        }

                        if mergedChildren.isEmpty {
                            Text("No other apps are merged into this one.")
                                .font(.system(size: 12))
                                .foregroundColor(ThemeColors.textDim)
                                .padding(.vertical, 4)
                        } else {
                            ForEach(mergedChildren) { child in
                                HStack {
                                    DeviceMarker(isLocal: child.deviceId.caseInsensitiveCompare(state.localDeviceId) == .orderedSame, size: 7)

                                    Text(child.displayName)
                                        .font(.system(size: 12, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)

                                    Spacer()

                                    Text(Formatters.hoursMinutes(seconds: child.totalSeconds))
                                        .font(.system(size: 11, design: .monospaced))
                                        .foregroundColor(ThemeColors.textMuted)

                                    Button(action: {
                                        unmerge(childId: child.id)
                                    }) {
                                        Image(systemName: "xmark")
                                            .font(.system(size: 10, weight: .bold))
                                            .foregroundColor(ThemeColors.textDim)
                                            .frame(width: 18, height: 18)
                                    }
                                    .buttonStyle(.plain)
                                }
                                .padding(8)
                                .background(ThemeColors.surface)
                                .cornerRadius(6)
                            }
                            Text("Merged time is added together in every view. Sessions keep their own device.")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }

                    Divider()
                        .foregroundColor(ThemeColors.border)

                    // Danger Actions
                    VStack(spacing: 8) {
                        Button(action: {
                            showingStopConfirm = true
                        }) {
                            HStack {
                                Image(systemName: "eye.slash")
                                    .font(.system(size: 12))
                                Text("Stop tracking this app")
                                    .font(.system(size: 12, weight: .medium))
                                Spacer()
                            }
                            .foregroundColor(ThemeColors.warning)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 8)
                            .background(ThemeColors.surface)
                            .cornerRadius(6)
                        }
                        .buttonStyle(.plain)

                        Button(action: {
                            showingClearConfirm = true
                        }) {
                            HStack {
                                Image(systemName: "trash")
                                    .font(.system(size: 12))
                                Text("Clear this app's history…")
                                    .font(.system(size: 12, weight: .medium))
                                Spacer()
                            }
                            .foregroundColor(ThemeColors.danger)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 8)
                            .background(ThemeColors.surface)
                            .cornerRadius(6)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(16)
            }

            // Save footer
            HStack {
                Spacer()

                Button(action: saveChanges) {
                    Text("Save changes")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 6)
                        .background(ThemeColors.accent)
                        .cornerRadius(6)
                }
                .buttonStyle(.plain)
            }
            .padding(14)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.borderSubtle),
                alignment: .top
            )
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .onAppear {
            loadDetails()
        }
        .sheet(isPresented: $showingMerge) {
            MergeView(state: state, mainAppId: appId) {
                showingMerge = false
                loadDetails()
            }
        }
        .alert("Stop tracking?", isPresented: $showingStopConfirm) {
            Button("Stop tracking", role: .destructive) {
                let lib = MacAppLibrary(db: state.db)
                try? lib.stopTracking(appId: appId)
                state.refresh()
                onDismiss()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("TimeSink will stop tracking new sessions for this app. Existing history will be kept.")
        }
        .alert("Clear history?", isPresented: $showingClearConfirm) {
            Button("Clear all history", role: .destructive) {
                let lib = MacAppLibrary(db: state.db)
                try? lib.clearHistory(appId: appId)
                state.refresh()
                loadDetails()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This will permanently delete all recorded sessions and daily usage for this app.")
        }
    }

    private func kindButton(title: String, val: AppKind) -> some View {
        Button(action: {
            kind = val
        }) {
            Text(title)
                .font(.system(size: 12, weight: kind == val ? .semibold : .regular))
                .foregroundColor(kind == val ? ThemeColors.background : ThemeColors.textPrimary)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
                .background(kind == val ? ThemeColors.accent : ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(kind == val ? ThemeColors.accent : ThemeColors.border, lineWidth: 1)
                )
        }
        .buttonStyle(.plain)
    }

    private func loadDetails() {
        let lib = MacAppLibrary(db: state.db)
        do {
            let stmt = try state.db.prepare("SELECT display_name, kind, is_background FROM apps WHERE id = ?1;")
            try stmt.bind(int64: appId, at: 1)
            if try stmt.step() {
                self.displayName = stmt.columnText(at: 0) ?? ""
                self.kind = AppKind(rawValue: stmt.columnInt64(at: 1)) ?? .other
                self.isBackground = stmt.columnInt64(at: 2) == 1
            }
            self.mergedChildren = try lib.getMergedChildren(mainId: appId)
        } catch {}
    }

    private func unmerge(childId: Int64) {
        let lib = MacAppLibrary(db: state.db)
        try? lib.unmerge(appId: childId)
        loadDetails()
        state.refresh()
    }

    private func saveChanges() {
        let lib = MacAppLibrary(db: state.db)
        try? lib.updateApp(appId: appId, displayName: displayName, kind: kind, isBackground: isBackground)
        state.refresh()
        onDismiss()
    }
}
