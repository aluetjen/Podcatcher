/**
 * This file is part of Podcatcher for N9.
 * Author: Johan Paul (johan.paul@d-pointer.com)
 *
 * Podcatcher for N9 is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Podcatcher for N9 is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Podcatcher for N9.  If not, see <http://www.gnu.org/licenses/>.
 */
import QtQuick 1.1
import com.nokia.symbian 1.1
import com.nokia.extras 1.1

Item {
    property int downloadedBytes
    property int totalBytes;

    width: downloadLabel.width + cancelButton.width
    height: podcastItem.height

    Label {
        id: downloadLabel
        anchors.left: parent.left
        anchors.verticalCenter: parent.verticalCenter
        font.pixelSize: 17
        text: "Downloading"
        width: parent.width - cancelButton.width
        color: "black"
    }

    ProgressBar {
        id: progressBar
        width: downloadLabel.width
        maximumValue: totalDownloadSize
        value: alreadyDownloadedSize
        anchors.left: downloadLabel.left
        anchors.top: downloadLabel.bottom
        anchors.topMargin: 5
    }

    Image {
        id: cancelButton
        source: "qrc:///gfx/cancel-podcast-download.png"
        anchors.right:  parent.right
        anchors.verticalCenter: downloadLabel.verticalCenter
        width: 18
        height: 18
    }

    MouseArea {
        anchors.fill: parent
        onClicked: {
            console.log("Cancel download of: " + channelId + "index: "+index);
            appWindow.cancelDownload(channelId, index);
        }
    }
}
