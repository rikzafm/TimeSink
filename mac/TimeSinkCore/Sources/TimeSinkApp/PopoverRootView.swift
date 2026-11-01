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
                // App Detail View takes over content
                AppDetailView(state: state)
            } else {
                // Main Header
                HeaderView(state: state)

                // Tab Selection Bar
                TabBarView(state: state)

                // Active Tab Content
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

                // Bottom bar
                HStack(alignment: .center) {
                    Button(action: {
                        state.showingAddApp = true
                    }) {
                        HStack(spacing: 5) {
                            Image(systemName: "plus")
                                .font(.system(size: 11, weight: .bold))
                            Text("Add app")
                                .font(.system(size: 12, weight: .medium))
                        }
                        .foregroundColor(ThemeColors.accent)
                    }
                    .buttonStyle(.plain)

                    Spacer()

                    tabFooterTrailingView()
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(ThemeColors.surface)
                .overlay(
                    Rectangle()
                        .frame(height: 1)
                        .foregroundColor(ThemeColors.borderSubtle),
                    alignment: .top
                )
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
    }

    @ViewBuilder
    private func tabFooterTrailingView() -> some View {
        switch state.selectedTab {
        case .today:
            if state.deviceScope == .all {
                Text("Updated just now · synced 2 min ago")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            } else {
                Text("Updated just now")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
        case .week:
            Text("Last backup · 1 day ago")
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
        case .month:
            Button(action: {
                // Export Month CSV
                state.exportMonthCsv()
            }) {
                Text("Export month (CSV)")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
            .buttonStyle(.plain)
        case .year:
            switch state.yearSubView {
            case .pace:
                Text("Tap a month")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            case .wrapped:
                Button(action: {
                    state.saveWrappedImage()
                }) {
                    Text("Save as image")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 5)
                        .background(ThemeColors.accent)
                        .cornerRadius(6)
                }
                .buttonStyle(.plain)
            case .matrix:
                Text("Tap an app or a month")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
        case .all:
            EmptyView()
        }
    }
}
