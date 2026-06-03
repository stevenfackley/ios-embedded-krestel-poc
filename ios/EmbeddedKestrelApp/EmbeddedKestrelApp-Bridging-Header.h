// EmbeddedKestrelApp-Bridging-Header.h
//
// Swift→C bridge. Anything imported here is visible to Swift as a global
// function. Wired up via the SWIFT_OBJC_BRIDGING_HEADER build setting
// (see ios/project.yml). Importing the backend header makes kestrel_start /
// kestrel_stop callable directly from Swift with no extra glue.

#import "KestrelBackend.h"
