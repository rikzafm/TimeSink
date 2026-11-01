import SwiftUI
import AppKit
import TimeSinkCore

public struct AppIconPickerView: View {
    @ObservedObject public var state: AppState

    @State private var previewId: String
    @State private var selectedStyle: IconStyle

    public init(state: AppState) {
        self.state = state
        _previewId = State(initialValue: state.appIcon)
        let style = IconCatalogue.find(state.appIcon)?.style ?? .clean
        _selectedStyle = State(initialValue: style)
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack(spacing: 6) {
                Button(action: {
                    state.showingAppIconPicker = false
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

                Text("App icon")
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
                VStack(alignment: .leading, spacing: 14) {
                    // Title & Subtitle
                    VStack(alignment: .leading, spacing: 2) {
                        Text("App icon")
                            .font(.system(size: 16, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)
                        Text("Pick how TimeSink looks in the menu bar.")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.textMuted)
                    }
                    .padding(.top, 4)

                    // Preview Card
                    previewCard

                    // Style Filter Chips
                    styleChips

                    // Icon Grid
                    iconGrid

                    // Action Buttons (Reset & Apply icon)
                    actionButtons
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
    }

    // MARK: - Subviews

    private var previewCard: some View {
        let entry = IconCatalogue.find(previewId) ?? IconCatalogue.all[0]

        return HStack(alignment: .center, spacing: 14) {
            // Big 72x72 preview
            ZStack {
                RoundedRectangle(cornerRadius: 12)
                    .fill(Color.black.opacity(0.15))
                    .frame(width: 72, height: 72)
                thumbView(id: previewId, size: 68)
            }
            .frame(width: 72, height: 72)

            VStack(alignment: .leading, spacing: 6) {
                // Name & Style Badge
                HStack(spacing: 8) {
                    Text(entry.name)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)

                    Text(entry.style.rawValue)
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(ThemeColors.onAccent)
                        .padding(.horizontal, 7)
                        .padding(.vertical, 2)
                        .background(Capsule().fill(ThemeColors.accent))
                }

                // Dark menu bar strip (16, 24, 32)
                HStack(spacing: 12) {
                    thumbView(id: previewId, size: 16)
                    thumbView(id: previewId, size: 24)
                    thumbView(id: previewId, size: 32)
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 6).fill(Color(red: 0.133, green: 0.133, blue: 0.133)))

                // Light menu bar strip (16, 24, 32)
                HStack(spacing: 12) {
                    thumbView(id: previewId, size: 16)
                    thumbView(id: previewId, size: 24)
                    thumbView(id: previewId, size: 32)
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 6).fill(Color(red: 0.918, green: 0.918, blue: 0.918)))
            }
        }
        .padding(14)
        .background(
            RoundedRectangle(cornerRadius: 10)
                .fill(ThemeColors.surfaceRaised)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .strokeBorder(ThemeColors.border, lineWidth: 1)
        )
    }

    private var styleChips: some View {
        HStack(spacing: 8) {
            ForEach(IconStyle.allCases, id: \.self) { style in
                let isSelected = selectedStyle == style
                Button(action: {
                    selectedStyle = style
                }) {
                    Text(style.rawValue)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(isSelected ? ThemeColors.onAccent : ThemeColors.textPrimary)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 6)
                        .background(
                            RoundedRectangle(cornerRadius: 6)
                                .fill(isSelected ? ThemeColors.accent : ThemeColors.surface)
                        )
                        .overlay(
                            RoundedRectangle(cornerRadius: 6)
                                .strokeBorder(isSelected ? Color.clear : ThemeColors.border, lineWidth: 1)
                        )
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.top, 2)
    }

    private var iconGrid: some View {
        let icons = IconCatalogue.inStyle(selectedStyle)
        let columns = [
            GridItem(.fixed(82), spacing: 10),
            GridItem(.fixed(82), spacing: 10),
            GridItem(.fixed(82), spacing: 10),
            GridItem(.fixed(82), spacing: 10)
        ]

        return LazyVGrid(columns: columns, spacing: 12) {
            ForEach(icons) { icon in
                let isPreviewed = icon.id == previewId
                let isApplied = icon.id == state.appIcon

                Button(action: {
                    previewId = icon.id
                }) {
                    VStack(spacing: 4) {
                        ZStack(alignment: .topTrailing) {
                            // Tile box
                            RoundedRectangle(cornerRadius: 12)
                                .fill(ThemeColors.surface)
                                .frame(width: 72, height: 72)
                                .overlay(
                                    RoundedRectangle(cornerRadius: 12)
                                        .strokeBorder(isPreviewed ? ThemeColors.accent : ThemeColors.border, lineWidth: 2)
                                )

                            // Centered icon image
                            thumbView(id: icon.id, size: 48)
                                .frame(width: 72, height: 72)

                            // Applied checkmark badge
                            if isApplied {
                                ZStack {
                                    Circle()
                                        .fill(ThemeColors.accent)
                                        .frame(width: 18, height: 18)
                                    Text("✓")
                                        .font(.system(size: 11, weight: .bold))
                                        .foregroundColor(ThemeColors.onAccent)
                                }
                                .offset(x: 4, y: -4)
                            }
                        }

                        Text(icon.name)
                            .font(.system(size: 11))
                            .foregroundColor(isPreviewed ? ThemeColors.accent : ThemeColors.textMuted)
                            .lineLimit(1)
                            .frame(maxWidth: 78)
                    }
                    .frame(width: 82)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.vertical, 4)
    }

    private var actionButtons: some View {
        let isModified = previewId != state.appIcon

        return HStack(spacing: 10) {
            // Reset Button
            Button(action: {
                previewId = IconCatalogue.defaultId
                if let entry = IconCatalogue.find(previewId) {
                    selectedStyle = entry.style
                }
            }) {
                Text("Reset")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundColor(ThemeColors.textPrimary)
                    .padding(.horizontal, 18)
                    .frame(height: 34)
                    .background(
                        RoundedRectangle(cornerRadius: 6)
                            .fill(ThemeColors.surface)
                    )
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .strokeBorder(ThemeColors.border, lineWidth: 1)
                    )
            }
            .buttonStyle(.plain)

            // Apply icon Button
            Button(action: {
                state.setAppIcon(previewId)
            }) {
                Text("Apply icon")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.onAccent)
                    .frame(maxWidth: .infinity)
                    .frame(height: 34)
                    .background(
                        RoundedRectangle(cornerRadius: 6)
                            .fill(ThemeColors.accent)
                    )
                    .opacity(isModified ? 1.0 : 0.45)
            }
            .buttonStyle(.plain)
            .disabled(!isModified)
        }
        .padding(.top, 8)
        .padding(.bottom, 12)
    }

    private func thumbView(id: String, size: CGFloat) -> some View {
        Group {
            if let img = IconCatalogue.image(for: id, size: Int(size), accentColor: NSColor(ThemeColors.accent)) {
                Image(nsImage: img)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .frame(width: size, height: size)
            } else {
                Color.clear.frame(width: size, height: size)
            }
        }
    }
}

