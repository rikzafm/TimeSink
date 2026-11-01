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

    private var isTall: Bool {
        state.selectedTab == .today || (state.selectedTab == .year && state.yearSubView == .wrapped) || state.selectedTab == .all
    }

    private var footerHeight: CGFloat {
        if state.selectedTab == .all { return 42 }
        return isTall ? 46 : 44
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
        .padding(.horizontal, isTall ? 16 : 14)
        .frame(height: footerHeight)
        .topRule()
    }

    private func muted(_ text: String, size: CGFloat = 12) -> some View {
        Text(text)
            .font(.system(size: size))
            .foregroundColor(ThemeColors.textMuted)
    }

    @ViewBuilder
    private var footerTrailing: some View {
        switch state.selectedTab {
        case .today:
            if state.deviceScope == .all && state.lastSyncUtc > 0 {
                muted("Updated just now · synced \(Formatters.timeAgo(state.lastSyncUtc))", size: 11)
            } else {
                muted("Updated just now", size: 11)
            }
        case .week:
            muted("Last backup · \(Formatters.timeAgo(state.lastBackupUtc))")
        case .month:
            Button(action: { state.exportMonthCsv() }) {
                Text("Export month (CSV)")
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textDim)
            }
            .buttonStyle(.plain)
        case .year:
            switch state.yearSubView {
            case .pace:
                muted("Tap a month")
            case .wrapped:
                Button(action: { state.saveWrappedImage() }) {
                    Text("Save as image")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 14)
                        .frame(height: 28)
                        .background(RoundedRectangle(cornerRadius: 8).fill(ThemeColors.accent))
                }
                .buttonStyle(.plain)
            case .matrix:
                muted("Tap an app or a month")
            }
        case .all:
            EmptyView()
        }
    }
}
