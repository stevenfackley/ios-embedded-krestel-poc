// KestrelBackend.h
//
// C declarations for the symbols exported by the NativeAOT static library
// (libKestrelBackend.a). NativeAOT emits the symbols but NOT a header, so we
// hand-author this and keep it byte-for-byte in sync with the
// [UnmanagedCallersOnly(EntryPoint = "...")] attributes in NativeBootstrap.cs.
//
// Boundary contract (enforced on the managed side):
//   • all parameters are blittable (int, uint8_t*);
//   • no managed exception ever unwinds into caller — errors surface as int codes;
//   • buffer-copy exports return -(bytes needed) when the caller's buffer is too small.

#ifndef KESTREL_BACKEND_H
#define KESTREL_BACKEND_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Starts the embedded loopback server on a background thread. Idempotent.
/// Pass port=0 to let the OS pick an ephemeral port; call kestrel_info() afterward
/// to discover the actual bound port.
/// @param port TCP port on 127.0.0.1 (0 = OS-assigned).
/// @return 0 on success; -1 on failure (call kestrel_last_error for details).
int kestrel_start(int port);

/// Stops the embedded server if it is running. Safe to call when not started.
void kestrel_stop(void);

/// Copies the last pipeline error as UTF-8 into buf.
/// Format: "type|message|correlationId" (pipe-separated).
/// @param buf    Caller-allocated buffer.
/// @param bufLen Capacity of buf in bytes.
/// @return Bytes written (>=1) on success; 0 if no error is buffered;
///         -(bytes needed) if buf is too small; -1 on internal error.
int kestrel_last_error(uint8_t* buf, int bufLen);

/// Copies a JSON diagnostic snapshot as UTF-8 into buf.
/// Fields: port, uptimeSeconds, requestsServed, dotnetVersion, os, processArch.
/// @param buf    Caller-allocated buffer (recommend >= 512 bytes).
/// @param bufLen Capacity of buf in bytes.
/// @return Bytes written on success; -(bytes needed) if buf is too small; -1 on error.
int kestrel_info(uint8_t* buf, int bufLen);

#ifdef __cplusplus
}
#endif

#endif /* KESTREL_BACKEND_H */
