import SwiftUI
import TimeSinkCore

public struct PopoverRootView: View {
    @ObservedObject public var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        VStack(spacing: 0) {
            if state.showingSettings {
                SettingsView(state: state)
            } else if state.showingAddApp {
                AddAppView(state: state) {
                    state.showingAddApp = false
                }
            } else if state.selectedAppDetailId != nil {
                AppDetailView(state: state)
            } else {
                HeaderView(state: state)
                TabBarView(state: state)

                Group {
                    switch state.selectedTab {
                    case .today:
                        TodayTabView(state: state)
                    case .week:
                        WeekTabView(state: state)
                    case .month:
                        MonthTabView(state: state)
                    case .year:
                        YearTabView(state: state)
                    case .all:
                        AllTabView(state: state)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)

                footer
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .font(.system(size: 12).monospacedDigit())
    }

    private var footer: some View {
        HStack {
            Button(action: { state.showingAddApp = true }) {
                Text("+ Add app")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.accent)
            }
            .buttonStyle(.plain)

            Spacer()

            footerTrailing
        }
        .padding(.horizontal, 14)
        .frame(height: 44)
        .topRule()
    }

    private func muted(_ text: String, size: CGFloat = 12) -> some View {
        Text(text)
            .font(.system(size: size))
            .foregroundColor(ThemeColors.textMuted)
    }

    private var footerTrailing: some View {
        FooterSyncStatusView(state: state)
    }
}

struct FooterSyncStatusView: View {
    @ObservedObject var state: AppState
    @State private var isSpinning: Bool = false

    var body: some View {
        Button(action: handleClick) {
            HStack(spacing: 5) {
                IconView(.sync, size: 12, weight: 2.0, color: statusColor)
                    .rotationEffect(.degrees(isSpinning ? 360 : 0))
                    .animation(
                        state.isSyncing
                            ? Animation.linear(duration: 1.0).repeatForever(autoreverses: false)
                            : .default,
                        value: isSpinning
                    )

                Text(statusText)
                    .font(.system(size: 11))
            }
            .foregroundColor(statusColor)
            .padding(.vertical, 2)
            .padding(.horizontal, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside {
                NSCursor.pointingHand.push()
            } else {
                NSCursor.pop()
            }
        }
        .onAppear {
            if state.isSyncing {
                isSpinning = true
            }
        }
        .onChange(of: state.isSyncing) { syncing in
            if syncing {
                isSpinning = true
            } else {
                isSpinning = false
            }
        }
    }

    private var statusText: String {
        if state.isSyncing {
            return "Syncing..."
        }
        if !state.syncEnabled {
            return "Sync off"
        }
        if state.syncState.lowercased().contains("error") {
            return "Sync error"
        }
        if state.lastSyncUtc <= 0 {
            return "Never"
        }
        return Formatters.timeAgo(state.lastSyncUtc)
    }

    private var statusColor: Color {
        if state.isSyncing {
            return ThemeColors.accent
        }
        if !state.syncEnabled {
            return ThemeColors.textMuted
        }
        if state.syncState.lowercased().contains("error") {
            return ThemeColors.warning
        }
        return ThemeColors.textMuted
    }

    private func handleClick() {
        if state.isSyncing { return }
        if state.syncEnabled && !state.syncFolder.isEmpty && FileManager.default.fileExists(atPath: state.syncFolder) && !state.syncState.lowercased().contains("error") {
            state.triggerSyncNow()
        } else {
            state.showingSettings = true
            state.showingDevicesSync = true
        }
    }
}
