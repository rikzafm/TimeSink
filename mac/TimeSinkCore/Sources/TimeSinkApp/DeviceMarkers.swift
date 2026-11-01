import SwiftUI

public enum DeviceKind {
    case localMac
    case remotePC
}

public struct DeviceMarker: View {
    public let isLocal: Bool
    public var size: CGFloat = 8

    public init(isLocal: Bool, size: CGFloat = 8) {
        self.isLocal = isLocal
        self.size = size
    }

    public var body: some View {
        if isLocal {
            Circle()
                .fill(ThemeColors.deviceA)
                .frame(width: size, height: size)
        } else {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(ThemeColors.deviceB)
                .frame(width: size, height: size)
        }
    }
}

public struct CompoundDeviceSwatch: View {
    public var width: CGFloat = 16
    public var height: CGFloat = 10

    public init(width: CGFloat = 16, height: CGFloat = 10) {
        self.width = width
        self.height = height
    }

    public var body: some View {
        ZStack(alignment: .leading) {
            RoundedRectangle(cornerRadius: 2)
                .fill(ThemeColors.deviceA)
                .frame(width: width * 0.7, height: height)

            RoundedRectangle(cornerRadius: 2)
                .fill(ThemeColors.deviceB)
                .frame(width: width * 0.7, height: height)
                .offset(x: width * 0.3)
        }
        .frame(width: width, height: height)
    }
}

public struct DeviceSummaryRowView: View {
    public let localName: String
    public let remoteName: String?
    public let localSeconds: Int64
    public let remoteSeconds: Int64
    public let onBothSeconds: Int64

    public init(
        localName: String = "Mac",
        remoteName: String? = "PC",
        localSeconds: Int64,
        remoteSeconds: Int64,
        onBothSeconds: Int64
    ) {
        self.localName = localName
        self.remoteName = remoteName
        self.localSeconds = localSeconds
        self.remoteSeconds = remoteSeconds
        self.onBothSeconds = onBothSeconds
    }

    public var body: some View {
        HStack(spacing: 8) {
            HStack(spacing: 4) {
                DeviceMarker(isLocal: true, size: 7)
                Text("\(localName) \(Formatters.hoursMinutes(seconds: localSeconds))")
                    .font(.system(size: 11, weight: .medium))
                    .foregroundColor(ThemeColors.textMuted)
            }

            if let remoteName = remoteName, remoteSeconds > 0 {
                Text("·")
                    .foregroundColor(ThemeColors.textDim)

                HStack(spacing: 4) {
                    DeviceMarker(isLocal: false, size: 7)
                    Text("\(remoteName) \(Formatters.hoursMinutes(seconds: remoteSeconds))")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(ThemeColors.textMuted)
                }
            }

            if onBothSeconds > 0 {
                Text("·")
                    .foregroundColor(ThemeColors.textDim)

                HStack(spacing: 4) {
                    CompoundDeviceSwatch(width: 12, height: 7)
                    Text("\(Formatters.hoursMinutes(seconds: onBothSeconds)) on both")
                        .font(.system(size: 11, weight: .regular))
                        .foregroundColor(ThemeColors.textDim)
                }
            }
        }
        .padding(.vertical, 3)
    }
}
