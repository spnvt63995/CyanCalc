//! Local C ABI for the pinned, unmodified IronCalc engine.
//! All allocations are released by the allocating library. Calls for one model
//! must be serialized by the managed owner. Panics poison that model, not the ABI.
mod legacy;
mod protocol;
mod style_validation;
mod upstream_api;
mod upstream_dispatch;
mod wasm_support;

use std::ffi::{c_char, CString};
use std::panic::{catch_unwind, AssertUnwindSafe};
use upstream_api::Model;

pub struct NativeWorkbook {
    model: Model,
    poisoned: bool,
}

fn response(result: Result<serde_json::Value, String>) -> *mut c_char {
    let value = match result {
        Ok(value) => serde_json::json!({ "ok": true, "value": value }),
        Err(error) => serde_json::json!({ "ok": false, "error": error }),
    };
    // JSON escapes embedded NUL characters; these conversions cannot contain NUL.
    CString::new(value.to_string())
        .expect("JSON contains no raw NUL")
        .into_raw()
}

#[no_mangle]
pub extern "C" fn ic_model_new() -> *mut NativeWorkbook {
    match catch_unwind(|| Model::new("Workbook", "en", "UTC", "en")) {
        Ok(Ok(model)) => Box::into_raw(Box::new(NativeWorkbook {
            model,
            poisoned: false,
        })),
        _ => std::ptr::null_mut(),
    }
}

/// # Safety
/// `handle` must be a live ic_model_new allocation exclusively held for this call.
/// `input` must remain readable for `length` bytes. Release the response with ic_string_free.
#[no_mangle]
pub unsafe extern "C" fn ic_model_call(
    handle: *mut NativeWorkbook,
    input: *const u8,
    length: usize,
) -> *mut c_char {
    // Rust slices must fit in isize. There is no application-level payload quota.
    if handle.is_null() || input.is_null() || length > isize::MAX as usize {
        return response(Err("Invalid native call buffer or handle".into()));
    }
    let workbook = &mut *handle;
    if workbook.poisoned {
        return response(Err(
            "Engine panicked earlier; close and reopen this document".into(),
        ));
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        let request = serde_json::from_slice(std::slice::from_raw_parts(input, length))
            .map_err(|error| format!("Invalid request JSON: {error}"))?;
        protocol::dispatch(&mut workbook.model, &request)
    }));
    response(match result {
        Ok(result) => result,
        Err(_) => {
            workbook.poisoned = true;
            Err("IronCalc panicked during this operation; document disabled".into())
        }
    })
}

/// # Safety
/// `input` must remain readable for `length` bytes. Release the response with ic_string_free.
/// This entry point never creates or accesses a workbook.
#[no_mangle]
pub unsafe extern "C" fn ic_utility_call(input: *const u8, length: usize) -> *mut c_char {
    if input.is_null() || length > isize::MAX as usize {
        return response(Err("Invalid native call buffer".into()));
    }
    let result = catch_unwind(AssertUnwindSafe(|| {
        let request = serde_json::from_slice(std::slice::from_raw_parts(input, length))
            .map_err(|error| format!("Invalid request JSON: {error}"))?;
        protocol::dispatch_utility(&request)
    }));
    response(match result {
        Ok(result) => result,
        Err(_) => Err("IronCalc panicked during this utility operation".into()),
    })
}

/// # Safety
/// `handle` must be null or a live ic_model_new allocation. Call exactly once,
/// after all other calls using the handle have completed.
#[no_mangle]
pub unsafe extern "C" fn ic_model_free(handle: *mut NativeWorkbook) {
    if !handle.is_null() {
        drop(Box::from_raw(handle));
    }
}

/// # Safety
/// `value` must be null or an unfreed response returned by ic_model_call or ic_utility_call.
/// Do not use the response after this call.
#[no_mangle]
pub unsafe extern "C" fn ic_string_free(value: *mut c_char) {
    if !value.is_null() {
        drop(CString::from_raw(value));
    }
}

#[cfg(test)]
mod abi_tests {
    use super::*;
    use std::ffi::CStr;

    unsafe fn read_response(pointer: *mut c_char) -> serde_json::Value {
        assert!(!pointer.is_null());
        let value = serde_json::from_slice(CStr::from_ptr(pointer).to_bytes()).unwrap();
        ic_string_free(pointer);
        value
    }

    #[test]
    fn buffers_above_old_64_mib_quota_are_accepted_without_weakening_pointer_checks() {
        let mut request = vec![b' '; 64 * 1024 * 1024 + 1];
        request.extend_from_slice(br#"{"op":"getName"}"#);
        unsafe {
            let model = ic_model_new();
            assert!(!model.is_null());
            let result = read_response(ic_model_call(model, request.as_ptr(), request.len()));
            assert_eq!(result, serde_json::json!({"ok":true,"value":"Workbook"}));
            request.truncate(64 * 1024 * 1024 + 1);
            request.extend_from_slice(br#"{"op":"columnNameFromNumber","column":1}"#);
            let result = read_response(ic_utility_call(request.as_ptr(), request.len()));
            assert_eq!(result, serde_json::json!({"ok":true,"value":"A"}));
            assert_eq!(
                read_response(ic_model_call(model, std::ptr::null(), 1))["ok"],
                false
            );
            assert_eq!(
                read_response(ic_model_call(model, request.as_ptr(), usize::MAX))["ok"],
                false
            );
            assert_eq!(
                read_response(ic_utility_call(request.as_ptr(), usize::MAX))["ok"],
                false
            );
            ic_model_free(model);
        }
    }
}
