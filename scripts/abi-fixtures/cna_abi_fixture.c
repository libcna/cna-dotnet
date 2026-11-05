// SPDX-License-Identifier: MIT

#include <stdint.h>

#if defined(_WIN32)
#define CNA_ABI_FIXTURE_EXPORT __declspec(dllexport)
#else
#define CNA_ABI_FIXTURE_EXPORT __attribute__((visibility("default")))
#endif

#ifndef CNA_ABI_FIXTURE_VERSION
#define CNA_ABI_FIXTURE_VERSION UINT32_C(0x00003C00)
#endif

#if !defined(CNA_ABI_FIXTURE_UNREADABLE_METADATA)
CNA_ABI_FIXTURE_EXPORT uint32_t cna_get_abi_version(void)
{
    return CNA_ABI_FIXTURE_VERSION;
}
#endif

#if defined(CNA_ABI_FIXTURE_CHANGED_SIGNATURE)
CNA_ABI_FIXTURE_EXPORT uint32_t cna_error_get_last_message_size(void)
{
    return UINT32_C(0);
}
#else
CNA_ABI_FIXTURE_EXPORT uint32_t cna_error_get_last_message_size(uint64_t* const out_bytes)
{
    if (out_bytes != 0) {
        *out_bytes = UINT64_C(0);
    }
    return UINT32_C(0);
}
#endif

CNA_ABI_FIXTURE_EXPORT uint32_t cna_touch_capabilities_init(void* const out_capabilities)
{
    uint8_t* const bytes = (uint8_t*)out_capabilities;
    uint32_t* const fields = (uint32_t*)out_capabilities;
    if (out_capabilities == 0) {
        return UINT32_C(2);
    }

#if defined(CNA_ABI_FIXTURE_INCOMPATIBLE_STRUCT)
    fields[0] = UINT32_C(24);
    fields[1] = UINT32_C(2);
    for (uint32_t index = UINT32_C(8); index < UINT32_C(24); ++index) {
        bytes[index] = UINT8_C(0);
    }
#else
    fields[0] = UINT32_C(16);
    fields[1] = UINT32_C(1);
    for (uint32_t index = UINT32_C(8); index < UINT32_C(16); ++index) {
        bytes[index] = UINT8_C(0);
    }
#endif
    return UINT32_C(0);
}

#if !defined(CNA_ABI_FIXTURE_MISSING_REQUIRED_SYMBOL)
CNA_ABI_FIXTURE_EXPORT void cna_game_destroy(void) {}
#endif

#define CNA_ABI_FIXTURE_STUB(name) CNA_ABI_FIXTURE_EXPORT void name(void) {}
#include "required_symbols.inc"

#if defined(CNA_ABI_FIXTURE_EXTRA_SYMBOL)
CNA_ABI_FIXTURE_EXPORT void cna_fixture_unrelated_future_symbol(void) {}
#endif
