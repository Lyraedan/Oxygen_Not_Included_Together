using System;
using System.Collections.Generic;
using System.Text;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Scripts.Duplicants;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class DuplicantTests
    {
        [UnitTest(name: "Duplicant is selected?", category: "Duplicant")]
        public static UnitTestResult HasDuplicantSelected()
        {
            var selected = SelectTool.Instance?.selected;
            if (selected == null)
                return UnitTestResult.Fail("No object selected");
            var hasDuplicant = selected.TryGetComponent(out MinionIdentity _);
            if (!hasDuplicant)
                return UnitTestResult.Fail("Selected object is not a duplicant");
            return UnitTestResult.Pass($"Selected object is the duplicant: {selected.name}");
        }
        
        [UnitTest(name: "MinionMultiplayerInitializer present", category: "Duplicant")]
        public static UnitTestResult MinionMultiplayerInitializerExists()
        {
            var selected = SelectTool.Instance?.selected;
            if (selected == null)
                return UnitTestResult.Fail("No object selected");
            if (!selected.TryGetComponent(out MinionMultiplayerInitializer _))
                return UnitTestResult.Fail("MinionMultiplayerInitializer not found on selected duplicant");
            return UnitTestResult.Pass("MinionMultiplayerInitializer is present");
        }

        [UnitTest(name: "Client init disables AI components", category: "Duplicant")]
        public static UnitTestResult ClientInitDisablesAI()
        {
            if (!MultiplayerSession.InActiveSession)
                return UnitTestResult.Fail("Not in a multiplayer session");
            if (!MultiplayerSession.IsClient)
                return UnitTestResult.Fail("Not a client");

            var selected = SelectTool.Instance?.selected;
            if (selected == null)
                return UnitTestResult.Fail("No object selected");
            if (!selected.TryGetComponent(out MinionMultiplayerInitializer _))
                return UnitTestResult.Fail("MinionMultiplayerInitializer not found");

            if (selected.TryGetComponent<ChoreDriver>(out var driver) && driver.enabled)
                return UnitTestResult.Fail("ChoreDriver is still enabled");
            if (selected.TryGetComponent<ChoreConsumer>(out var consumer) && consumer.enabled)
                return UnitTestResult.Fail("ChoreConsumer is still enabled");
            if (selected.TryGetComponent<MinionBrain>(out var brain) && brain.enabled)
                return UnitTestResult.Fail("MinionBrain is still enabled");
            if (selected.TryGetComponent<Sensors>(out var sensors) && sensors.enabled)
                return UnitTestResult.Fail("Sensors is still enabled");
            if (!selected.TryGetComponent<ClientReceiver_ChoreErrands>(out _))
                return UnitTestResult.Fail("ClientReceiver_ChoreErrands not found");

            return UnitTestResult.Pass("AI disabled, ClientReceiver_ChoreErrands present");
        }

        [UnitTest(name: "Host init adds sync components", category: "Duplicant")]
        public static UnitTestResult HostInitAddsSyncComponents()
        {
            if (!MultiplayerSession.InActiveSession)
                return UnitTestResult.Fail("Not in a multiplayer session");
            if (!MultiplayerSession.IsHost)
                return UnitTestResult.Fail("Not the host");

            var selected = SelectTool.Instance?.selected;
            if (selected == null)
                return UnitTestResult.Fail("No object selected");
            if (!selected.TryGetComponent(out MinionMultiplayerInitializer _))
                return UnitTestResult.Fail("MinionMultiplayerInitializer not found");

            if (!selected.TryGetComponent<DuplicantStateSender>(out _))
                return UnitTestResult.Fail("DuplicantStateSender not found");
            if (!selected.TryGetComponent<DuplicantChoreBroadcaster>(out _))
                return UnitTestResult.Fail("DuplicantChoreBroadcaster not found");

            return UnitTestResult.Pass("DuplicantStateSender and DuplicantChoreBroadcaster present");
        }

        [UnitTest(name: "BaseMinion tag guard", category: "Duplicant")]
        public static UnitTestResult BaseMinionTagGuard()
        {
            var selected = SelectTool.Instance?.selected;
            if (selected == null)
                return UnitTestResult.Fail("No object selected");

            var kpref = selected.GetComponent<KPrefabID>();
            if (kpref == null)
                return UnitTestResult.Fail("Selected object has no KPrefabID");
            if (!kpref.HasTag(GameTags.BaseMinion))
                return UnitTestResult.Fail("Selected object does not have BaseMinion tag — init would bail");

            return UnitTestResult.Pass("Selected object has BaseMinion tag");
        }
    }
}
