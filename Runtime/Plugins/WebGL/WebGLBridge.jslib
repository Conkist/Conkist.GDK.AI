mergeInto(LibraryManager.library, {
    WebGLBridge_PostMessage: function (methodNamePtr, dataPtr) {
        var methodName = UTF8ToString(methodNamePtr);
        var data = UTF8ToString(dataPtr);
        
        // Dispatch custom event to window or call a global function
        if (typeof window.dispatchReactUnityEvent === 'function') {
            window.dispatchReactUnityEvent(methodName, data);
        } else if (typeof window[methodName] === 'function') {
            window[methodName](data);
        } else {
            // Fallback: dispatch custom event to window
            var event = new CustomEvent(methodName, { detail: data });
            window.dispatchEvent(event);
        }
    }
});
