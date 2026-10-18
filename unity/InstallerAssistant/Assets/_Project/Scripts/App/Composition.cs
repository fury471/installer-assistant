using System.IO;
using InstallerAssistant.AI;
using InstallerAssistant.AR;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using UnityEngine;

namespace InstallerAssistant.App
{
    /// <summary>
    /// Composition root: the one place that decides which implementation stands behind each contract.
    /// Owner: package C. Adding a new implementation (say a second wire reader) means changing this file only.
    ///
    ///   Wizard of Oz   an IServiceSource on the InstallerApp's GameObject (WizardOfOzRig) builds everything:
    ///                  photos, scripted readings, in-memory records, operator controls.
    ///   Live           ArCameraView (B) · QrLabelScanner, PerceptionWireReader, PerceptionSecondOpinion (A) ·
    ///                  JsonRecordStore (C).
    /// </summary>
    public static class Composition
    {
        public static ServiceSet Build(InstallerApp app)
        {
            if (app.mode == AppMode.WizardOfOz)
            {
                var source = app.GetComponent<IServiceSource>();
                var set = source != null ? source.CreateServices(app) : null;
                if (set != null) return set;
                Debug.LogError("[InstallerAssistant] Mode is Wizard of Oz, but there is no WizardOfOzRig on '" + app.name +
                    "'. Add one to the same GameObject, or set the mode to Live. Running in live mode instead.");
            }
            return Live(app);
        }

        public static ServiceSet Live(InstallerApp app)
        {
            var view = Object.FindAnyObjectByType<ArCameraView>();
            if (view == null)
            {
                view = app.gameObject.AddComponent<ArCameraView>();
                Debug.LogWarning("[InstallerAssistant] No ArCameraView in the scene: added one to '" + app.name + "' (webcam fallback until the AR setup exists).");
            }
            var client = new PerceptionClient(app.perceptionUrl);
            QrLabelScanner.DebugPayload = app.testLabel ?? "";
            string file = Path.Combine(Application.persistentDataPath, app.recordsFile);
            return new ServiceSet
            {
                View = view,
                Labels = new QrLabelScanner(),
                Wires = new PerceptionWireReader(client),
                SecondOpinion = new PerceptionSecondOpinion(client),
                Records = new JsonRecordStore(file),
                Operator = null,
                Style = null,
                Description = "Live mode · camera: " + view.Describe() + " · perception: " + app.perceptionUrl + " · records: " + file,
            };
        }
    }
}
