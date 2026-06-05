// CapabilityScreenshotTests.swift — end-to-end UI smoke test that exercises the
// embedded NativeAOT server through the SwiftUI front end and captures a
// screenshot at each step. The headline assertion is the SQLCipher
// encryption-at-rest proof: we drive Capabilities → SQLCipher → Run and verify
// the live `output` object renders, then keep the screenshots as attachments.
//
// Run (simulator, no signing needed):
//   xcodebuild test -project EmbeddedKestrel.xcodeproj -scheme EmbeddedKestrelApp \
//     -sdk iphonesimulator -destination 'platform=iOS Simulator,name=iPhone 17' \
//     CODE_SIGNING_ALLOWED=NO
//
// Extract the PNGs afterwards from the .xcresult with `xcrun xcresulttool`.

import XCTest

final class CapabilityScreenshotTests: XCTestCase {

    override func setUp() {
        super.setUp()
        continueAfterFailure = false
    }

    func testSqlcipherProofIsVisibleInApp() throws {
        let app = XCUIApplication()
        app.launch()

        // 1) Dashboard — embedded server should report Running before we proceed.
        XCTAssertTrue(
            app.staticTexts["Running"].waitForExistence(timeout: 20),
            "Embedded server never reached Running state on the Dashboard")
        attach(app, "01-dashboard")

        // 2) Capabilities tab — descriptors are fetched from the embedded server.
        let capabilitiesTab = app.tabBars.buttons["Capabilities"]
        XCTAssertTrue(capabilitiesTab.waitForExistence(timeout: 10), "Capabilities tab missing")
        capabilitiesTab.tap()

        // The TOC populates asynchronously; the SQLCipher row only exists if the
        // rebuilt xcframework's server actually serves the persist.sqlcipher probe.
        let sqlcipherRow = app.staticTexts["SQLCipher encryption-at-rest"]
        XCTAssertTrue(
            waitForElement(sqlcipherRow, scrollHost: app, timeout: 30),
            "SQLCipher row never appeared — descriptors not loaded or probe missing")
        attach(app, "02-capabilities-toc")

        // 3) Detail page (before running the probe).
        sqlcipherRow.tap()
        let runButton = app.buttons["Run probe"]
        XCTAssertTrue(runButton.waitForExistence(timeout: 10), "Run probe button missing on detail")
        attach(app, "03-sqlcipher-detail")

        // 4) Run the probe and wait for completion (button flips to "Run again").
        runButton.tap()
        let runAgain = app.buttons["Run again"]
        XCTAssertTrue(runAgain.waitForExistence(timeout: 30), "Probe did not complete")
        attach(app, "04-sqlcipher-result")

        // 5) The proof itself: the server-defined `output` object must render the
        //    SQLCipher flags. Scroll the Output section into view, then verify and
        //    capture it.
        let cipherVersionRow = app.staticTexts["cipherVersion"]
        XCTAssertTrue(
            waitForElement(cipherVersionRow, scrollHost: app, timeout: 10),
            "Output section (cipherVersion) not rendered after run")
        XCTAssertTrue(app.staticTexts["wrongKeyRejected"].exists, "wrongKeyRejected flag missing")
        XCTAssertTrue(app.staticTexts["headerIsCiphertext"].exists, "headerIsCiphertext flag missing")
        XCTAssertTrue(app.staticTexts["roundTripOk"].exists, "roundTripOk flag missing")
        attach(app, "05-sqlcipher-proof")
    }

    // MARK: - Helpers

    /// Poll for an element to exist, then scroll the host up until it is hittable
    /// (List rows exist off-screen, so existence alone isn't enough to tap/shoot).
    private func waitForElement(_ element: XCUIElement,
                                scrollHost: XCUIApplication,
                                timeout: TimeInterval) -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if element.exists { break }
            usleep(500_000)
        }
        guard element.exists else { return false }

        var scrolls = 0
        while !element.isHittable && scrolls < 12 {
            scrollHost.swipeUp()
            scrolls += 1
        }
        return element.exists
    }

    /// Capture the app's current screen and keep it even when the test passes.
    private func attach(_ app: XCUIApplication, _ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
