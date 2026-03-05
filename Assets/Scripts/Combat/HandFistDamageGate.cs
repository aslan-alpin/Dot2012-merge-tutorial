using UnityEngine;
using UnityEngine.XR;

namespace VRCombat.Combat
{
    [DisallowMultipleComponent]
    public class HandFistDamageGate : MonoBehaviour, IDamageGate
    {
        [SerializeField]
        XRNode m_HandNode = XRNode.LeftHand;

        [SerializeField]
        [Range(0.05f, 0.98f)]
        float m_GripThreshold = 0.62f;

        [SerializeField]
        [Range(0.05f, 0.98f)]
        float m_TriggerThreshold = 0.55f;

        InputDevice m_Device;
        float m_LastGripValue;
        bool m_LastGripButtonState;
        float m_LastTriggerValue;
        bool m_LastTriggerButtonState;

        public void Configure(XRNode handNode, float gripThreshold)
        {
            m_HandNode = handNode;
            m_GripThreshold = Mathf.Clamp(gripThreshold, 0.05f, 0.98f);
            RefreshDevice();
            ReadCurrentState();
        }

        void OnEnable()
        {
            RefreshDevice();
            ReadCurrentState();
        }

        void Update()
        {
            if (!m_Device.isValid)
                RefreshDevice();

            ReadCurrentState();
        }

        public bool CanDealDamage()
        {
            return m_LastGripButtonState ||
                   m_LastGripValue >= m_GripThreshold ||
                   m_LastTriggerButtonState ||
                   m_LastTriggerValue >= m_TriggerThreshold;
        }

        void RefreshDevice()
        {
            m_Device = InputDevices.GetDeviceAtXRNode(m_HandNode);
        }

        void ReadCurrentState()
        {
            if (!m_Device.isValid)
            {
                m_LastGripValue = 0f;
                m_LastGripButtonState = false;
                m_LastTriggerValue = 0f;
                m_LastTriggerButtonState = false;
                return;
            }

            if (!m_Device.TryGetFeatureValue(CommonUsages.grip, out m_LastGripValue))
                m_LastGripValue = 0f;

            if (!m_Device.TryGetFeatureValue(CommonUsages.gripButton, out m_LastGripButtonState))
                m_LastGripButtonState = false;

            if (!m_Device.TryGetFeatureValue(CommonUsages.trigger, out m_LastTriggerValue))
                m_LastTriggerValue = 0f;

            if (!m_Device.TryGetFeatureValue(CommonUsages.triggerButton, out m_LastTriggerButtonState))
                m_LastTriggerButtonState = false;
        }
    }
}
