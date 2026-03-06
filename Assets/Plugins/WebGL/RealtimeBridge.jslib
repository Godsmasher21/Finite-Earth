mergeInto(LibraryManager.library, {
  FE_WS_Connect: function (urlPtr, gameObjectNamePtr, onOpenPtr, onMessagePtr, onClosePtr) {
    var url = UTF8ToString(urlPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var onOpen = UTF8ToString(onOpenPtr);
    var onMessage = UTF8ToString(onMessagePtr);
    var onClose = UTF8ToString(onClosePtr);

    if (!window.__finiteEarthRealtime) {
      window.__finiteEarthRealtime = {};
    }

    if (window.__finiteEarthRealtime.socket) {
      try {
        window.__finiteEarthRealtime.socket.close();
      } catch (e) {
        // Ignore close failure.
      }
    }

    try {
      var socket = new WebSocket(url);
      window.__finiteEarthRealtime.socket = socket;
      window.__finiteEarthRealtime.gameObjectName = gameObjectName;
      window.__finiteEarthRealtime.onOpen = onOpen;
      window.__finiteEarthRealtime.onMessage = onMessage;
      window.__finiteEarthRealtime.onClose = onClose;

      socket.onopen = function () {
        SendMessage(gameObjectName, onOpen, "");
      };

      socket.onmessage = function (event) {
        var payload = typeof event.data === "string" ? event.data : JSON.stringify(event.data);
        SendMessage(gameObjectName, onMessage, payload);
      };

      socket.onclose = function (event) {
        var reason = event && event.reason ? event.reason : "closed";
        SendMessage(gameObjectName, onClose, reason);
      };

      socket.onerror = function () {
        SendMessage(gameObjectName, onClose, "error");
      };
    } catch (error) {
      SendMessage(gameObjectName, onClose, String(error));
    }
  },

  FE_WS_Send: function (payloadPtr) {
    var payload = UTF8ToString(payloadPtr);
    if (!window.__finiteEarthRealtime || !window.__finiteEarthRealtime.socket) {
      return;
    }

    var socket = window.__finiteEarthRealtime.socket;
    if (socket.readyState === WebSocket.OPEN) {
      socket.send(payload);
    }
  },

  FE_WS_Close: function () {
    if (!window.__finiteEarthRealtime || !window.__finiteEarthRealtime.socket) {
      return;
    }

    try {
      window.__finiteEarthRealtime.socket.close();
    } catch (e) {
      // Ignore close failure.
    }
  }
});
