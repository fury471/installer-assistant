using System;
using InstallerAssistant.AI;
using InstallerAssistant.AR;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.App
{
    /// <summary>Which implementations the app runs on. The flow and screens are the same in both.</summary>
    public enum AppMode
    {
        /// <summary>Photos and scripted readings (needs a WizardOfOzRig): demo, user-study fallback, work without hardware.</summary>
        WizardOfOz,
        /// <summary>The product: AR camera (package B), perception and AI (package A), stored records (package C).</summary>
        Live,
    }

    /// <summary>Everything the app needs from the outside, behind the frozen contracts.</summary>
    public sealed class ServiceSet
    {
        public ICameraView View;              // B: what the camera shows, frames, anchors
        public ILabelScanner Labels;          // A: QR label decoding
        public IWireReader Wires;             // A: colours and positions of the wires
        public ISecondOpinion SecondOpinion;  // A: independent AI comparison
        public IRecordStore Records;          // C: both ends of every cable
        /// <summary>Operator controls of the Wizard of Oz; null in live mode.</summary>
        public IStudyOperator Operator;
        /// <summary>Overlay look for this mode (marker size, tag placement); null = the app's own settings.</summary>
        public OverlayStyle Style;
        public string Description = "";
    }

    /// <summary>Overlay settings that depend on the camera image (photo resolution, zoom) rather than on the app.</summary>
    [Serializable]
    public sealed class OverlayStyle
    {
        [Range(0.5f, 1f)] public float markerScale = 0.8f;
        [Range(0.5f, 1f)] public float labelTextScale = 1f;
        [Tooltip("Put the tags below a row of terminals when there is room.")]
        public bool tagsBelowTerminals = true;
        [Tooltip("Snapshot of the other end in the top-left corner (else top-right).")]
        public bool snapshotOnLeft = true;
        public float snapshotInset = 40f;
        public float snapshotTopInset = 320f;
        [Tooltip("Say \"cable label\" instead of \"QR label\" (hand-written labels in the bench photos).")]
        public bool handwrittenLabels;
    }

    /// <summary>
    /// Something in the scene that can supply the services for the Wizard of Oz (WizardOfOzRig). Composition looks
    /// for it on the InstallerApp's GameObject, so the App folder never depends on the WizardOfOz folder.
    /// </summary>
    public interface IServiceSource
    {
        /// <summary>Builds the services; null if it cannot (the app then falls back to live mode).</summary>
        ServiceSet CreateServices(InstallerApp app);
    }

    /// <summary>
    /// The operator's side of the Wizard of Oz: what a study leader controls behind the scenes. Implemented by
    /// WizardOfOzRig; the app calls it only when it is not null, so live mode never depends on it.
    /// </summary>
    public interface IStudyOperator
    {
        /// <summary>Which end the participant is at when they first press "Scan a cable".</summary>
        CableEnd StartEnd { get; set; }
        string[] FaultNames { get; }
        int Fault { get; }
        /// <summary>Plants a fault on the bench. The caller restarts the session afterwards.</summary>
        void ChooseFault(int index);
        /// <summary>The bench starts without the controller label, to show the label guidance.</summary>
        bool StartWithoutLabel { get; set; }
        bool ShowOperatorKeys { get; set; }

        /// <summary>A fresh site visit: the bench is as planted again (not re-wired, label as configured).</summary>
        void SessionReset();
        /// <summary>A check starts (the scripted readers are unsure again on their first reading).</summary>
        void BeginCheck();
        /// <summary>The participant attached the missing label.</summary>
        void LabelAttached();
        /// <summary>The participant re-wired the controller end to match this device-end record.</summary>
        void Rewired(CableRecord deviceEnd);
        /// <summary>The participant re-wired the device end to match the controller end (a fix done where they stand).</summary>
        void RewiredAtDevice();

        /// <summary>
        /// Operator shortcut "skip to the second end": saves the start end in 'store' as the bench is planted.
        /// Returns the label of that end (its End says which end was saved) and the end's snapshot.
        /// </summary>
        CableLabel Prefill(IRecordStore store, out EndSnapshot snapshot);
    }
}
