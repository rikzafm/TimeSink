import SwiftUI
import TimeSinkCore

public struct AppSettingsSheet: View {
    @ObservedObject var state: AppState
    public let appId: Int64
    public let onDismiss: () -> Void

    @State private var displayName: String = "Chrome"
    @State private var kind: AppKind = .other
    @State private var isBackground: Bool = false
    @State private var mergedChildren: [TrackedAppItem] = []
    @State private var showingMerge: Bool = false
    @State private var showingStopConfirm: Bool = false
    @State private var showingClearConfirm: Bool = false
    @State private var appPath: String = "/Applications/Google Chrome.app"
    @State private var firstTrackedDate: String = "14 Mar 2026"
    @State private var totalTrackedHours: String = "283 h"

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
                        Text("App settings")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                }
                .buttonStyle(.plain)

                Spacer()

                Text("Changes save as you make them")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            .background(ThemeColors.surface)

            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    // Top App Hero Row
                    HStack(spacing: 12) {
                        ZStack {
                            RoundedRectangle(cornerRadius: 8)
                                .fill(ThemeColors.accent)
                                .frame(width: 40, height: 40)

                            Text(String(displayName.prefix(1)).uppercased())
                                .font(.system(size: 20, weight: .bold))
                                .foregroundColor(ThemeColors.background)
                        }

                        VStack(alignment: .leading, spacing: 3) {
                            TextField("App Name", text: $displayName)
                                .textFieldStyle(.plain)
                                .font(.system(size: 15, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)
                                .padding(.horizontal, 8)
                                .padding(.vertical, 4)
                                .background(ThemeColors.surface)
                                .cornerRadius(6)
                                .overlay(
                                    RoundedRectangle(cornerRadius: 6)
                                        .stroke(ThemeColors.border, lineWidth: 1)
                                )
                                .onChange(of: displayName) { _, _ in
                                    saveChanges()
                                }

                            Text("\(totalTrackedHours) tracked · since \(firstTrackedDate)")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        Spacer()
                    }
                    .padding(.top, 4)

                    // Setting item: Game type
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Game type")
                                    .font(.system(size: 13, weight: .medium))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Used by Year › Wrapped")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            HStack(spacing: 0) {
                                ForEach([("Other", AppKind.other), ("Game", AppKind.game), ("Gacha", AppKind.gacha)], id: \.0) { item in
                                    Button(action: {
                                        kind = item.1
                                        saveChanges()
                                    }) {
                                        Text(item.0)
                                            .font(.system(size: 11, weight: kind == item.1 ? .semibold : .regular))
                                            .foregroundColor(kind == item.1 ? ThemeColors.textPrimary : ThemeColors.textMuted)
                                            .padding(.horizontal, 10)
                                            .padding(.vertical, 4)
                                            .background(kind == item.1 ? ThemeColors.surfaceRaised : Color.clear)
                                            .cornerRadius(4)
                                    }
                                    .buttonStyle(.plain)
                                }
                            }
                            .padding(2)
                            .background(ThemeColors.surface)
                            .cornerRadius(6)
                            .overlay(
                                RoundedRectangle(cornerRadius: 6)
                                    .stroke(ThemeColors.border, lineWidth: 1)
                            )
                        }
                    }
                    .padding(10)
                    .background(ThemeColors.surface)
                    .cornerRadius(6)

                    // Setting item: Background
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Background")
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)
                            Text("Runs all day. Still counted, but ranked after your other apps")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        Spacer()

                        Toggle("", isOn: $isBackground)
                            .toggleStyle(SwitchToggleStyle(tint: ThemeColors.accent))
                            .labelsHidden()
                            .onChange(of: isBackground) { _, _ in
                                saveChanges()
                            }
                    }
                    .padding(10)
                    .background(ThemeColors.surface)
                    .cornerRadius(6)

                    // Setting item: Icon
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Icon")
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)
                            Text("3 icons found for this app")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        Spacer()

                        Button(action: {}) {
                            Text("Change")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(ThemeColors.accent)
                        }
                        .buttonStyle(.plain)
                    }
                    .padding(10)
                    .background(ThemeColors.surface)
                    .cornerRadius(6)

                    // MERGED APPS Section
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("MERGED APPS")
                                .font(.system(size: 10, weight: .bold))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            Button(action: {
                                showingMerge = true
                            }) {
                                Text("Merge…")
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                            .buttonStyle(.plain)
                        }

                        // Main app row
                        HStack(spacing: 8) {
                            Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)

                            VStack(alignment: .leading, spacing: 1) {
                                Text("Google Chrome")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Mac · com.google.Chrome · main app")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            Text("124 h")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)
                        }
                        .padding(8)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)

                        // Child 1
                        HStack(spacing: 8) {
                            RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)

                            VStack(alignment: .leading, spacing: 1) {
                                Text("chrome.exe")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("PC · Desktop PC · merged")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            Text("159 h")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)

                            Button(action: {}) {
                                Image(systemName: "xmark")
                                    .font(.system(size: 10, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                    .frame(width: 16, height: 16)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(8)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)

                        // Child 2
                        HStack(spacing: 8) {
                            Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)

                            VStack(alignment: .leading, spacing: 1) {
                                Text("Chrome Beta")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Mac · com.google.Chrome.beta · merged")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            Text("0 h")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)

                            Button(action: {}) {
                                Image(systemName: "xmark")
                                    .font(.system(size: 10, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                    .frame(width: 16, height: 16)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(8)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)

                        Text("Merged time is added together in every view. Sessions keep their own device.")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                            .lineSpacing(2)
                    }

                    // ABOUT Section
                    VStack(alignment: .leading, spacing: 8) {
                        Text("ABOUT")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)

                        VStack(spacing: 6) {
                            HStack {
                                Text("Path")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                Spacer()
                                Text(appPath)
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            Divider().background(ThemeColors.borderSubtle)

                            HStack {
                                Text("First tracked")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                Spacer()
                                Text(firstTrackedDate)
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }
                        }
                        .padding(10)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)
                    }

                    // Danger Actions
                    HStack(spacing: 10) {
                        Button(action: {
                            showingStopConfirm = true
                        }) {
                            Text("Stop tracking")
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(Color(red: 0.95, green: 0.45, blue: 0.45))
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 8)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(6)
                        }
                        .buttonStyle(.plain)

                        Button(action: {
                            showingClearConfirm = true
                        }) {
                            Text("Clear history…")
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(Color(red: 0.95, green: 0.45, blue: 0.45))
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 8)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(6)
                        }
                        .buttonStyle(.plain)
                    }
                    .padding(.top, 4)
                }
                .padding(16)
            }
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

    private func loadDetails() {
        let lib = MacAppLibrary(db: state.db)
        do {
            let stmt = try state.db.prepare("SELECT display_name, kind, is_background, exe_path, first_seen_utc FROM apps WHERE id = ?1;")
            try stmt.bind(int64: appId, at: 1)
            if try stmt.step() {
                if let name = stmt.columnText(at: 0), !name.isEmpty {
                    self.displayName = name
                }
                self.kind = AppKind(rawValue: stmt.columnInt64(at: 1)) ?? .other
                self.isBackground = stmt.columnInt64(at: 2) == 1
                if let path = stmt.columnText(at: 3), !path.isEmpty {
                    self.appPath = path
                }
            }
            self.mergedChildren = try lib.getMergedChildren(mainId: appId)
        } catch {}
    }

    private func saveChanges() {
        let lib = MacAppLibrary(db: state.db)
        try? lib.updateApp(appId: appId, displayName: displayName, kind: kind, isBackground: isBackground)
        state.refresh()
    }
}
