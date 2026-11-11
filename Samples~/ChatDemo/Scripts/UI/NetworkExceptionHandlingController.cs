using UnityEngine;
using Conkist.GDK;

namespace Conkist.GDK.AI
{
    /// <summary>
    /// A sample exception handler that listens to network status updates 
    /// and triggers visual popup modal alerts via OverlayManager.
    /// </summary>
    [AddComponentMenu("Conkist/SmartAgents/NetworkExceptionHandlingController")]
    public class NetworkExceptionHandlingController : MonoBehaviour, EventListener<NetworkStatusEvent>
    {
        private bool _isPopupShowing = false;

        private void OnEnable()
        {
            this.Subscribe<NetworkStatusEvent>();
        }

        private void OnDisable()
        {
            this.Unsubscribe<NetworkStatusEvent>();
        }

        public void OnEventCallback(NetworkStatusEvent eventData)
        {
            if (!eventData.IsConnected)
            {
                if (!_isPopupShowing)
                {
                    _isPopupShowing = true;
                    Debug.LogWarning("[NetworkExceptionHandlingController] Internet connection lost. Displaying error modal...");
                    
                    EventManager.TriggerEvent(new ShowPopupEvent(
                        title: "Conexão Perdida",
                        message: "Não foi possível estabelecer uma conexão com a internet. Por favor, verifique suas configurações de rede.",
                        confirmText: "Tentar Novamente",
                        cancelText: "",
                        onConfirm: () => {
                            _isPopupShowing = false;
                            // Re-trigger connectivity check immediately
                            if (NetworkConnectivityMonitor.HasInstance)
                            {
                                NetworkConnectivityMonitor.Instance.StartMonitoring();
                            }
                        },
                        onCancel: null,
                        isError: true
                    ));
                }
            }
            else
            {
                if (_isPopupShowing)
                {
                    _isPopupShowing = false;
                    Debug.Log("[NetworkExceptionHandlingController] Connection restored. Closing modal overlay...");
                    EventManager.TriggerEvent(new HidePopupEvent());
                }
            }
        }
    }
}
