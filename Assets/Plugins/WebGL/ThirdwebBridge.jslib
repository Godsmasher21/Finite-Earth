mergeInto(LibraryManager.library, {
  FE_ConnectWalletWithStrategy: function (gameObjectNamePtr, strategyPtr, callbackMethodPtr) {
    var strategy = strategyPtr ? UTF8ToString(strategyPtr) : "injected";
    if (!strategy) {
      strategy = "injected";
    }

    var ensureBridge = function () {
      if (window.FiniteEarthBridge) {
        return;
      }

      window.FiniteEarthBridge = {
        connectWallet: function (selectedStrategy) {
          var mode = (selectedStrategy || "injected").toLowerCase();
          if (mode !== "injected") {
            return Promise.reject("Social login requires the web/bridge Thirdweb bundle to be loaded.");
          }

          if (!window.ethereum) {
            return Promise.reject("No injected wallet provider found.");
          }

          return window.ethereum.request({ method: "eth_requestAccounts" }).then(function (accounts) {
            if (!accounts || !accounts.length) {
              throw new Error("No wallet account returned.");
            }

            return accounts[0];
          });
        },
        requestSiweMessage: function (walletAddress, nonce, domain) {
          var now = new Date().toISOString();
          return Promise.resolve(
            domain + " wants you to sign in with your Ethereum account:\n" +
            walletAddress + "\n\n" +
            "Finite Earth wallet authentication\n\n" +
            "URI: https://" + domain + "\n" +
            "Version: 1\n" +
            "Chain ID: 6342\n" +
            "Nonce: " + nonce + "\n" +
            "Issued At: " + now
          );
        },
        signMessage: function (message) {
          if (!window.ethereum) {
            return Promise.reject("No injected wallet provider found.");
          }

          return window.ethereum.request({ method: "eth_accounts" }).then(function (accounts) {
            if (!accounts || !accounts.length) {
              throw new Error("No active wallet account.");
            }

            return window.ethereum.request({
              method: "personal_sign",
              params: [message, accounts[0]]
            });
          });
        },
        getActiveAddress: function () {
          if (!window.ethereum) {
            return Promise.reject("No injected wallet provider found.");
          }

          return window.ethereum.request({ method: "eth_accounts" }).then(function (accounts) {
            if (!accounts || !accounts.length) {
              throw new Error("No active wallet account.");
            }

            return accounts[0];
          });
        }
      };
    };

    ensureBridge();

    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    window.__finiteEarthUnityGameObject = gameObjectName;

    var send = function (payload) {
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    };

    if (!window.FiniteEarthBridge || !window.FiniteEarthBridge.connectWallet) {
      send({ ok: false, error: "FiniteEarthBridge.connectWallet is unavailable." });
      return;
    }

    window.FiniteEarthBridge.connectWallet(strategy)
      .then(function (address) { send({ ok: true, value: address }); })
      .catch(function (err) { send({ ok: false, error: String(err) }); });
  },

  FE_ConnectWallet: function (gameObjectNamePtr, callbackMethodPtr) {
    LibraryManager.library.FE_ConnectWalletWithStrategy(gameObjectNamePtr, 0, callbackMethodPtr);
  },

  FE_RequestSiweMessage: function (walletAddressPtr, noncePtr, domainPtr, callbackMethodPtr) {
    var walletAddress = UTF8ToString(walletAddressPtr);
    var nonce = UTF8ToString(noncePtr);
    var domain = UTF8ToString(domainPtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var gameObjectName = window.__finiteEarthUnityGameObject || "GameManager";

    var send = function (payload) {
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    };

    if (!window.FiniteEarthBridge || !window.FiniteEarthBridge.requestSiweMessage) {
      send({ ok: false, error: "FiniteEarthBridge.requestSiweMessage is unavailable." });
      return;
    }

    window.FiniteEarthBridge.requestSiweMessage(walletAddress, nonce, domain)
      .then(function (message) { send({ ok: true, value: message }); })
      .catch(function (err) { send({ ok: false, error: String(err) }); });
  },

  FE_SignMessage: function (messagePtr, callbackMethodPtr) {
    var message = UTF8ToString(messagePtr);
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var gameObjectName = window.__finiteEarthUnityGameObject || "GameManager";

    var send = function (payload) {
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    };

    if (!window.FiniteEarthBridge || !window.FiniteEarthBridge.signMessage) {
      send({ ok: false, error: "FiniteEarthBridge.signMessage is unavailable." });
      return;
    }

    window.FiniteEarthBridge.signMessage(message)
      .then(function (signature) { send({ ok: true, value: signature }); })
      .catch(function (err) { send({ ok: false, error: String(err) }); });
  },

  FE_GetActiveAddress: function (callbackMethodPtr) {
    var callbackMethod = UTF8ToString(callbackMethodPtr);
    var gameObjectName = window.__finiteEarthUnityGameObject || "GameManager";

    var send = function (payload) {
      SendMessage(gameObjectName, callbackMethod, JSON.stringify(payload));
    };

    if (!window.FiniteEarthBridge || !window.FiniteEarthBridge.getActiveAddress) {
      send({ ok: false, error: "FiniteEarthBridge.getActiveAddress is unavailable." });
      return;
    }

    window.FiniteEarthBridge.getActiveAddress()
      .then(function (address) { send({ ok: true, value: address }); })
      .catch(function (err) { send({ ok: false, error: String(err) }); });
  }
});
