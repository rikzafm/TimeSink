import SwiftUI
import TimeSinkCore

public struct PopoverRootView: View {
    @ObservedObject public var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Main Header
            HeaderView(state: state)

            if state.selectedAppDetailId != nil {
                // App Detail View takes over content
                AppDetailView(state: state)
            } else {
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

                // Bottom bar with "+ Add app"
                HStack {
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

                    Text(state.localDeviceName)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
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
}
