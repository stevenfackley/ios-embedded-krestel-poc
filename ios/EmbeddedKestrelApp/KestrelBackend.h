// KestrelBackend.h
//
// C declarations for the symbols exported by the NativeAOT static library
// (libKestrelBackend.a). NativeAOT emits the symbols but NOT a header, so we
// hand-author this and keep it byte-for-byte in sync with the
// [UnmanagedCallersOnly(EntryPoint = "...")] attributes in NativeBootstrap.cs.
//
// The signatures MUST use only blittable types (int) — that is the contract of
// the managed/native boundary on the .NET side.

#ifndef KESTREL_BACKEND_H
#define KESTREL_BACKEND_H

#ifdef __cplusplus
extern "C" {
#endif

/// Starts the embedded loopback server. Idempotent and non-blocking: it returns
/// as soon as the listener is up, so it is safe to call during app launch.
/// @param port TCP port on 127.0.0.1 (the PoC uses 5001).
/// @return 0 on success; -1 on failure.
int kestrel_start(int port);

/// Stops the embedded server if it is running. Safe to call when not started.
void kestrel_stop(void);

#ifdef __cplusplus
}
#endif

#endif /* KESTREL_BACKEND_H */
