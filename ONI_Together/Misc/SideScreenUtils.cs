using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ONI_Together.Misc
{
    public static class SideScreenUtils
    {
        /// <summary>
        /// Attempt to refresh the specific SideScreen on the gameobject.
        /// </summary>
        /// <param name="screen"></param>
        /// <param name="go"></param>
        public static void TryRefreshSideScreen(SideScreenContent screen, GameObject go)
        {
            // Condensed otherwise this thing would be 500 lines on its own
            switch (screen)
            {
                case ThresholdSwitchSideScreen ts: TryRefreshThresholdSwitchSideScreen(ts, go); return;
                case ValveSideScreen vs: TryRefreshValveSideScreen(vs, go); return;
                case TimerSideScreen timer: TryRefreshTimerSideScreen(timer, go); return;
                case CapacityControlSideScreen cap: TryRefreshCapacityControlSideScreen(cap, go); return;
                case ActiveRangeSideScreen ar: TryRefreshActiveRangeSideScreen(ar, go); return;
                case DoorToggleSideScreen door: TryRefreshDoorToggleSideScreen(door, go); return;
                case PlayerControlledToggleSideScreen toggle: TryRefreshPlayerControlledToggleSideScreen(toggle, go); return;
                case SingleSliderSideScreen sss: TryRefreshSingleSliderSideScreen(sss, go); return;
                case IntSliderSideScreen iss: TryRefreshIntSliderSideScreen(iss, go); return;
                case MultiSliderSideScreen mss: TryRefreshMultiSliderSideScreen(mss, go); return;
                case LimitValveSideScreen lv: TryRefreshLimitValveSideScreen(lv, go); return;
                case AlarmSideScreen alarm: TryRefreshAlarmSideScreen(alarm, go); return;
                case CounterSideScreen counter: TryRefreshCounterSideScreen(counter, go); return;
                case GeoTunerSideScreen geoTuner when go.GetSMI<GeoTuner.Instance>() != null: TryRefreshGeoTunerSideScreen(geoTuner, go); return;
                case RemoteWorkTerminalSidescreen rwt when go.TryGetComponent(out RemoteWorkTerminal _): TryRefreshRemoteWorkTerminalSidescreen(rwt, go); return;
                case CheckboxListGroupSideScreen cbGroup: TryRefreshCheckboxListGroupSideScreen(cbGroup, go); return;
                case RelatedEntitiesSideScreen related: TryRefreshRelatedEntitiesSideScreen(related, go); return;
                case TreeFilterableSideScreen treeFilter when go.TryGetComponent(out TreeFilterable _): TryRefreshTreeFilterableSideScreen(treeFilter, go); return;
                case FilterSideScreen filter when go.TryGetComponent(out Filterable _): TryRefreshFilterSideScreen(filter, go); return;

                case IncubatorSideScreen incubator when go.TryGetComponent(out EggIncubator _): TryRefreshIncubatorSideScreen(incubator, go); return;
                case PlanterSideScreen planter when go.TryGetComponent(out PlantablePlot _): TryRefreshPlanterSideScreen(planter, go); return;
                case ReceptacleSideScreen receptacle when go.TryGetComponent(out SingleEntityReceptacle _): TryRefreshReceptacleSideScreen(receptacle, go); return;
                case TimeRangeSideScreen timeRange when go.TryGetComponent(out LogicTimeOfDaySensor _): TryRefreshTimeRangeSideScreen(timeRange, go); return;
                case CritterSensorSideScreen critter when go.TryGetComponent(out LogicCritterCountSensor _): TryRefreshCritterSensorSideScreen(critter, go); return;
                case AutomatableSideScreen automatable when go.TryGetComponent(out Automatable _): TryRefreshAutomatableSideScreen(automatable, go); return;
                case SingleCheckboxSideScreen checkbox: TryRefreshSingleCheckboxSideScreen(checkbox, go); return;
                case AccessControlSideScreen access when go.TryGetComponent(out AccessControl _): TryRefreshAccessControlSideScreen(access, go); return;
                case CometDetectorSideScreen comet: TryRefreshCometDetectorSideScreen(comet, go); return;
                case FlatTagFilterSideScreen flatTag when go.TryGetComponent(out FlatTagFilterable _): TryRefreshFlatTagFilterSideScreen(flatTag, go); return;
                case MissileSelectionSideScreen missile: TryRefreshMissileSelectionSideScreen(missile, go); return;
                case ArtableSelectionSideScreen artable: TryRefreshArtableSelectionSideScreen(artable, go); return;
                case AssignableSideScreen assignable: TryRefreshAssignableSideScreen(assignable, go); return;
                case ComplexFabricatorSideScreen fabricator: TryRefreshComplexFabricatorSideScreen(fabricator, go); return;
                case MinionTodoSideScreen todo: TryRefreshMinionTodoSideScreen(todo, go); return;
                case SuitLockerSideScreen suit when go.TryGetComponent(out SuitLocker _): TryRefreshSuitLockerSideScreen(suit, go); return;

                case TemperatureSwitchSideScreen temp when go.TryGetComponent(out TemperatureControlledSwitch _): TryRefreshTemperatureSwitchSideScreen(temp, go); return;
                case DualSliderSideScreen dual: TryRefreshDualSliderSideScreen(dual, go); return;
                case DispenserSideScreen dispenser when go.TryGetComponent(out IDispenser _): TryRefreshDispenserSideScreen(dispenser, go); return;
                case SealedDoorSideScreen sealedDoor when go.TryGetComponent(out Door _): TryRefreshSealedDoorSideScreen(sealedDoor, go); return;
                case ProgressBarSideScreen progress when go.TryGetComponent(out IProgressBarSideScreen _): TryRefreshProgressBarSideScreen(progress, go); return;
                case RailGunSideScreen rail when go.TryGetComponent(out RailGun _): TryRefreshRailGunSideScreen(rail, go); return;
                case LogicBitSelectorSideScreen bit when go.TryGetComponent(out ILogicRibbonBitSelector _): TryRefreshLogicBitSelectorSideScreen(bit, go); return;
                case LogicBroadcastChannelSideScreen channel when go.TryGetComponent(out LogicBroadcastReceiver _): TryRefreshLogicBroadcastChannelSideScreen(channel, go); return;
                case ClusterLocationFilterSideScreen loc when go.TryGetComponent(out LogicClusterLocationSensor _): TryRefreshClusterLocationFilterSideScreen(loc, go); return;
                case GeneShufflerSideScreen gene when go.TryGetComponent(out GeneShuffler _): TryRefreshGeneShufflerSideScreen(gene, go); return;
                case LureSideScreen lure when go.TryGetComponent(out CreatureLure _): TryRefreshLureSideScreen(lure, go); return;
                case ConfigureConsumerSideScreen consumer when go.TryGetComponent(out IConfigurableConsumer _): TryRefreshConfigureConsumerSideScreen(consumer, go); return;
                case HighEnergyParticleDirectionSideScreen hepDir: TryRefreshHighEnergyParticleDirectionSideScreen(hepDir, go); return;
                case ClusterDestinationSideScreen clusterDest: TryRefreshClusterDestinationSideScreen(clusterDest, go); return;
                case CommandModuleSideScreen cmd when go.TryGetComponent(out LaunchConditionManager _): TryRefreshCommandModuleSideScreen(cmd, go); return;

                case SummonCrewSideScreen summon: TryRefreshSummonCrewSideScreen(summon, go); return;
                case AssignPilotAndCrewSideScreen pilot: TryRefreshAssignPilotAndCrewSideScreen(pilot, go); return;
                case RocketInteriorSectionSideScreen interior: TryRefreshRocketInteriorSectionSideScreen(interior, go); return;
                case LaunchPadSideScreen pad when go.TryGetComponent(out LaunchPad _): TryRefreshLaunchPadSideScreen(pad, go); return;
                case WarpPortalSideScreen warp when go.TryGetComponent(out WarpPortal _): TryRefreshWarpPortalSideScreen(warp, go); return;
                case SelfDestructButtonSideScreen selfDestruct when go.TryGetComponent(out CraftModuleInterface _): TryRefreshSelfDestructButtonSideScreen(selfDestruct, go); return;
                case ResearchSideScreen research when go.TryGetComponent(out ResearchCenter _): TryRefreshResearchSideScreen(research, go); return;
                case TelescopeSideScreen telescope when go.TryGetComponent(out Telescope _): TryRefreshTelescopeSideScreen(telescope, go); return;
                case ConditionListSideScreen condition when go.TryGetComponent(out IProcessConditionSet _): TryRefreshConditionListSideScreen(condition, go); return;
                case MonumentSideScreen monument when go.TryGetComponent(out MonumentPart _): TryRefreshMonumentSideScreen(monument, go); return;
                case RocketRestrictionSideScreen restrict when go.TryGetComponent(out RocketControlStation _): TryRefreshRocketRestrictionSideScreen(restrict, go); return;
                case ButtonMenuSideScreen btnMenu: TryRefreshButtonMenuSideScreen(btnMenu, go); return;
                case NToggleSideScreen nToggle when go.TryGetComponent(out INToggleSideScreenControl _): TryRefreshNToggleSideScreen(nToggle, go); return;
                case FewOptionSideScreen fewOpt: TryRefreshFewOptionSideScreen(fewOpt, go); return;
                case CargoModuleSideScreen cargo when go.TryGetComponent(out Clustercraft _): TryRefreshCargoModuleSideScreen(cargo, go); return;
                case ModuleFlightUtilitySideScreen flightUtil when go.TryGetComponent(out Clustercraft _): TryRefreshModuleFlightUtilitySideScreen(flightUtil, go); return;
                case PrinterceptorSideScreen printercept: TryRefreshPrinterceptorSideScreen(printercept, go); return;
                case BaseGameImpactorImperativeSideScreen impactor: TryRefreshBaseGameImpactorImperativeSideScreen(impactor, go); return;
                case GeneticAnalysisStationSideScreen genetic: TryRefreshGeneticAnalysisStationSideScreen(genetic, go); return;
                case ArtifactAnalysisSideScreen artifact: TryRefreshArtifactAnalysisSideScreen(artifact, go); return;

                case LaunchButtonSideScreen launch: TryRefreshLaunchButtonSideScreen(launch, go); return;
                case LoreBearerSideScreen lore when go.TryGetComponent(out LoreBearer _): TryRefreshLoreBearerSideScreen(lore, go); return;
                case OwnablesSidescreen ownable: TryRefreshOwnablesSidescreen(ownable, go); return;
                case PixelPackSideScreen pixel when go.TryGetComponent(out PixelPack _): TryRefreshPixelPackSideScreen(pixel, go); return;
                case RocketModuleSideScreen rocketMod when go.TryGetComponent(out ReorderableBuilding _): TryRefreshRocketModuleSideScreen(rocketMod, go); return;
                case SingleItemSelectionSideScreen singleItem: TryRefreshSingleItemSelectionSideScreen(singleItem, go); return;
                case SpecialCargoBayClusterSideScreen specialCargo: TryRefreshSpecialCargoBayClusterSideScreen(specialCargo, go); return;
                case TagFilterScreen tagFilter when go.TryGetComponent(out TreeFilterable _): TryRefreshTagFilterScreen(tagFilter, go); return;
                case TelepadSideScreen telepad when go.TryGetComponent(out Telepad _): TryRefreshTelepadSideScreen(telepad, go); return;
                case TemporalTearSideScreen temporal: TryRefreshTemporalTearSideScreen(temporal, go); return;
                case BionicSideScreen bionic when go.GetSMI<BionicBatteryMonitor.Instance>() != null: TryRefreshBionicSideScreen(bionic, go); return;

                case AutoPlumberSideScreen autoPlumb: TryRefreshAutoPlumberSideScreen(autoPlumb, go); return;
                case ClusterGridWorldSideScreen gridWorld: TryRefreshClusterGridWorldSideScreen(gridWorld, go); return;
                case HabitatModuleSideScreen habitat: TryRefreshHabitatModuleSideScreen(habitat, go); return;
                case NoConfigSideScreen noConfig: TryRefreshNoConfigSideScreen(noConfig, go); return;
                case RoleStationSideScreen roleStation: TryRefreshRoleStationSideScreen(roleStation, go); return;
            }

            DetailsScreen.Instance.RefreshTitle();
        }

        public static void RefreshSliderSets(List<SliderSet> sliderSets, Func<int, float> getValue)
        {
            if (sliderSets == null) return;
            for (int i = 0; i < sliderSets.Count; i++)
            {
                var set = sliderSets[i];
                if (set == null) continue;
                float val = getValue(i);
                if (set.valueSlider != null) set.valueSlider.value = val;
                if (set.numberInput != null)
                    set.numberInput.SetDisplayValue((Mathf.Round(val * 10f) / 10f).ToString());
            }
        }

        public static void TryRefreshThresholdSwitchSideScreen(ThresholdSwitchSideScreen ts, GameObject go)
        {
            if (!go.TryGetComponent(out IThresholdSwitch thresholdComp)) return;
            if (ts.thresholdSlider != null)
                ts.thresholdSlider.value = ts.thresholdSlider.GetPercentageFromValue(thresholdComp.Threshold);
            if (ts.numberInput != null)
                ts.numberInput.SetDisplayValue(thresholdComp.Format(thresholdComp.Threshold, false) + thresholdComp.ThresholdValueUnits());
            if (ts.unitsLabel != null)
                ts.unitsLabel.text = (string)thresholdComp.ThresholdValueUnits();
            if (ts.belowToggle != null && ts.aboveToggle != null)
            {
                ts.belowToggle.isOn = thresholdComp.ActivateAboveThreshold;
                ts.aboveToggle.isOn = !thresholdComp.ActivateAboveThreshold;
            }
        }

        public static void TryRefreshValveSideScreen(ValveSideScreen vs, GameObject go)
        {
            if (!go.TryGetComponent(out Valve valveComp)) return;
            if (vs.flowSlider != null)
                vs.flowSlider.value = valveComp.DesiredFlow;
            if (vs.numberInput != null)
                vs.numberInput.SetDisplayValue(GameUtil.GetFormattedMass(
                    Mathf.Max(0f, valveComp.DesiredFlow),
                    GameUtil.TimeSlice.PerSecond,
                    GameUtil.MetricMassFormat.Gram, false, "{0:0.#####}"));
        }

        public static void TryRefreshTimerSideScreen(TimerSideScreen timer, GameObject go)
        {
            if (!go.TryGetComponent(out LogicTimerSensor timerComp)) return;
            bool cyclesMode = timerComp.displayCyclesMode;
            if (cyclesMode)
            {
                if (timer.onDurationSlider != null) timer.onDurationSlider.value = timerComp.onDuration / 600f;
                if (timer.offDurationSlider != null) timer.offDurationSlider.value = timerComp.offDuration / 600f;
                if (timer.onDurationNumberInput != null) timer.onDurationNumberInput.SetAmount(timerComp.onDuration / 600f);
                if (timer.offDurationNumberInput != null) timer.offDurationNumberInput.SetAmount(timerComp.offDuration / 600f);
            }
            else
            {
                if (timer.onDurationSlider != null) timer.onDurationSlider.value = timerComp.onDuration;
                if (timer.offDurationSlider != null) timer.offDurationSlider.value = timerComp.offDuration;
                if (timer.onDurationNumberInput != null) timer.onDurationNumberInput.SetAmount(timerComp.onDuration);
                if (timer.offDurationNumberInput != null) timer.offDurationNumberInput.SetAmount(timerComp.offDuration);
            }
            timer.ReconfigureRingVisuals();
        }

        public static void TryRefreshCapacityControlSideScreen(CapacityControlSideScreen cap, GameObject go)
        {
            if (!go.TryGetComponent(out IUserControlledCapacity capComp)) return;
            if (cap.slider != null) cap.slider.value = capComp.UserMaxCapacity;
            if (cap.numberInput != null) cap.numberInput.SetDisplayValue(capComp.UserMaxCapacity.ToString());
            if (cap.unitsLabel != null) cap.unitsLabel.text = (string)capComp.CapacityUnits;
        }

        public static void TryRefreshActiveRangeSideScreen(ActiveRangeSideScreen ar, GameObject go)
        {
            if (!go.TryGetComponent(out IActivationRangeTarget arComp)) return;
            if (ar.activateValueSlider != null) ar.activateValueSlider.value = arComp.ActivateValue;
            if (ar.deactivateValueSlider != null) ar.deactivateValueSlider.value = arComp.DeactivateValue;
            if (ar.activateValueLabel != null) ar.activateValueLabel.SetDisplayValue(arComp.ActivateValue.ToString());
            if (ar.deactivateValueLabel != null) ar.deactivateValueLabel.SetDisplayValue(arComp.DeactivateValue.ToString());
        }

        public static void TryRefreshDoorToggleSideScreen(DoorToggleSideScreen door, GameObject go)
        {
            if (!go.TryGetComponent(out Door doorComp)) return;
            door.Refresh();
        }

        public static void TryRefreshPlayerControlledToggleSideScreen(PlayerControlledToggleSideScreen toggle, GameObject go)
        {
            if (!go.TryGetComponent(out IPlayerControlledToggle toggleComp)) return;
            bool state = toggleComp.ToggleRequested ? !toggleComp.ToggledOn() : toggleComp.ToggledOn();
            toggle.UpdateVisuals(state, false);
        }

        public static void TryRefreshSingleSliderSideScreen(SingleSliderSideScreen sss, GameObject go)
        {
            if (!(go.TryGetComponent(out ISingleSliderControl sssComp) || (sssComp = go.GetSMI<ISingleSliderControl>()) != null)) return;
            RefreshSliderSets(sss.sliderSets, idx => sssComp.GetSliderValue(idx));
        }

        public static void TryRefreshIntSliderSideScreen(IntSliderSideScreen iss, GameObject go)
        {
            if (!(go.TryGetComponent(out IIntSliderControl issComp) || (issComp = go.GetSMI<IIntSliderControl>()) != null)) return;
            RefreshSliderSets(iss.sliderSets, idx => issComp.GetSliderValue(idx));
        }

        public static void TryRefreshMultiSliderSideScreen(MultiSliderSideScreen mss, GameObject go)
        {
            if (!go.TryGetComponent(out IMultiSliderControl mssComp)) return;
            if (mss.sliderSets != null && mssComp.sliderControls != null)
            {
                int count = Mathf.Min(mss.sliderSets.Count, mssComp.sliderControls.Length);
                for (int i = 0; i < count; i++)
                {
                    var set = mss.sliderSets[i];
                    var ctrl = mssComp.sliderControls[i];
                    if (set == null || ctrl == null) continue;
                    float val = ctrl.GetSliderValue(i);
                    if (set.valueSlider != null) set.valueSlider.value = val;
                    if (set.numberInput != null)
                        set.numberInput.SetDisplayValue((Mathf.Round(val * 10f) / 10f).ToString());
                }
            }
        }

        public static void TryRefreshLimitValveSideScreen(LimitValveSideScreen lv, GameObject go)
        {
            if (!go.TryGetComponent(out LimitValve lvComp)) return;
            if (lv.limitSlider != null) lv.limitSlider.value = lv.limitSlider.GetPercentageFromValue(lvComp.Limit);
            if (lv.numberInput != null)
            {
                if (lvComp.displayUnitsInsteadOfMass)
                    lv.numberInput.SetDisplayValue(GameUtil.GetFormattedUnits(
                        Mathf.Max(0f, lvComp.Limit), displaySuffix: false,
                        floatFormatOverride: LimitValveSideScreen.FLOAT_FORMAT));
                else
                    lv.numberInput.SetDisplayValue(GameUtil.GetFormattedMass(
                        Mathf.Max(0f, lvComp.Limit),
                        massFormat: GameUtil.MetricMassFormat.Kilogram,
                        includeSuffix: false, floatFormat: LimitValveSideScreen.FLOAT_FORMAT));
            }
            lv.UpdateAmountLabel();
        }

        public static void TryRefreshAlarmSideScreen(AlarmSideScreen alarm, GameObject go)
        {
            if (!go.TryGetComponent(out LogicAlarm alarmComp)) return;
            if (alarm.nameInputField != null) alarm.nameInputField.SetDisplayValue(alarmComp.notificationName);
            if (alarm.tooltipInputField != null) alarm.tooltipInputField.SetDisplayValue(alarmComp.notificationTooltip);
            if (alarm.pauseCheckmark != null) alarm.pauseCheckmark.enabled = alarmComp.pauseOnNotify;
            if (alarm.zoomCheckmark != null) alarm.zoomCheckmark.enabled = alarmComp.zoomOnNotify;
        }

        public static void TryRefreshCounterSideScreen(CounterSideScreen counter, GameObject go)
        {
            if (!go.TryGetComponent(out LogicCounter counterComp)) return;
            if (counter.maxCountInput != null) counter.maxCountInput.SetAmount((float)counterComp.maxCount);
            if (counter.advancedModeCheckmark != null) counter.advancedModeCheckmark.enabled = counterComp.advancedMode;
        }

        public static void TryRefreshGeoTunerSideScreen(GeoTunerSideScreen geoTuner, GameObject go)
        {
            geoTuner.RefreshOptions();
        }

        public static void TryRefreshRemoteWorkTerminalSidescreen(RemoteWorkTerminalSidescreen rwt, GameObject go)
        {
            rwt.RefreshOptions();
        }

        public static void TryRefreshCheckboxListGroupSideScreen(CheckboxListGroupSideScreen cbGroup, GameObject go)
        {
            cbGroup.Refresh();
        }

        public static void TryRefreshRelatedEntitiesSideScreen(RelatedEntitiesSideScreen related, GameObject go)
        {
            related.RefreshOptions();
        }

        public static void TryRefreshTreeFilterableSideScreen(TreeFilterableSideScreen treeFilter, GameObject go)
        {
            foreach (var kvp in treeFilter.tagRowMap)
            {
                var row = kvp.Value;
                row.RefreshRowElements();
                row.UpdateCheckBoxVisualState();
            }
            treeFilter.UpdateAllCheckBoxVisualState();
        }

        public static void TryRefreshFilterSideScreen(FilterSideScreen filter, GameObject go)
        {
            if (filter.targetFilterable == null) return;

            filter.RefreshUI();
        }

        public static void TryRefreshTimeRangeSideScreen(TimeRangeSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out LogicTimeOfDaySensor sensor)) return;
            screen.startTime.value = sensor.startTime;
            screen.duration.value = sensor.duration;
            screen.labelValueStart.text = GameUtil.GetFormattedPercent(sensor.startTime * 100f);
            screen.labelValueDuration.text = GameUtil.GetFormattedPercent(sensor.duration * 100f);
            screen.imageActiveZone.rectTransform.rotation = Quaternion.identity;
            screen.imageActiveZone.rectTransform.Rotate(0f, 0f, 360f * sensor.startTime);
            screen.imageActiveZone.fillAmount = sensor.duration;
            screen.endIndicator.rotation = Quaternion.identity;
            screen.endIndicator.Rotate(0f, 0f, 360f * (sensor.startTime + sensor.duration));
        }

        public static void TryRefreshCritterSensorSideScreen(CritterSensorSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out LogicCritterCountSensor sensor)) return;
            screen.crittersCheckmark.enabled = sensor.countCritters;
            screen.eggsCheckmark.enabled = sensor.countEggs;
        }

        public static void TryRefreshAutomatableSideScreen(AutomatableSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out Automatable automatable)) return;
            screen.allowManualToggle.isOn = !automatable.GetAutomationOnly();
            screen.allowManualToggleCheckMark.enabled = screen.allowManualToggle.isOn;
        }

        public static void TryRefreshSingleCheckboxSideScreen(SingleCheckboxSideScreen screen, GameObject go)
        {
            ICheckboxControl control = go.GetComponent<ICheckboxControl>() ?? go.GetSMI<ICheckboxControl>();
            if (control == null) return;
            screen.label.text = control.CheckboxLabel;
            screen.toggle.isOn = control.GetCheckboxValue();
            screen.toggleCheckMark.enabled = screen.toggle.isOn;
        }

        public static void TryRefreshIncubatorSideScreen(IncubatorSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out EggIncubator incubator)) return;
            screen.continuousToggle.ChangeState(incubator.autoReplaceEntity ? 0 : 1);
        }

        // Net set
        public static void TryRefreshAccessControlSideScreen(AccessControlSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshCometDetectorSideScreen(CometDetectorSideScreen screen, GameObject go)
        {
            screen.RefreshOptions();
        }

        public static void TryRefreshFlatTagFilterSideScreen(FlatTagFilterSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshMissileSelectionSideScreen(MissileSelectionSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshPlanterSideScreen(PlanterSideScreen screen, GameObject go)
        {
            screen.RefreshSubspeciesToggles();
        }

        // TODO: Implement
        public static void TryRefreshReceptacleSideScreen(ReceptacleSideScreen screen, GameObject go)
        {
            //screen.UpdateState(null);
        }

        public static void TryRefreshArtableSelectionSideScreen(ArtableSelectionSideScreen screen, GameObject go)
        {
            screen.RefreshButtons();
        }

        // TODO: Implement
        public static void TryRefreshAssignableSideScreen(AssignableSideScreen screen, GameObject go)
        {
            //screen.Refresh();
        }

        public static void TryRefreshComplexFabricatorSideScreen(ComplexFabricatorSideScreen screen, GameObject go)
        {
            //screen.RefreshQueueDisplay();
        }

        public static void TryRefreshMinionTodoSideScreen(MinionTodoSideScreen screen, GameObject go)
        {
            screen.PopulateElements();
        }

        public static void TryRefreshSuitLockerSideScreen(SuitLockerSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out SuitLocker locker)) return;
            bool configured = locker.smi.sm.isConfigured.Get(locker.smi);
            screen.initialConfigScreen.SetActive(!configured);
            screen.regularConfigScreen.SetActive(configured);
            bool hasSuit = locker.GetStoredOutfit() != null;
            bool waiting = locker.smi.sm.isWaitingForSuit.Get(locker.smi);
            screen.regularConfigRequestSuitButton.isInteractable = !hasSuit;
            screen.regularConfigRequestSuitButton.GetComponentInChildren<LocText>().text = waiting
                ? global::STRINGS.UI.UISIDESCREENS.SUIT_SIDE_SCREEN.CONFIG_CANCEL_REQUEST
                : global::STRINGS.UI.UISIDESCREENS.SUIT_SIDE_SCREEN.CONFIG_REQUEST_SUIT;
            screen.regularConfigDropSuitButton.isInteractable = hasSuit;
        }

        public static void TryRefreshTemperatureSwitchSideScreen(TemperatureSwitchSideScreen screen, GameObject go)
        {
            screen.UpdateLabels();
            screen.UpdateTargetTemperatureLabel();
        }

        public static void TryRefreshDualSliderSideScreen(DualSliderSideScreen screen, GameObject go)
        {
            if (!(go.TryGetComponent(out IDualSliderControl comp) || (comp = go.GetSMI<IDualSliderControl>()) != null)) return;
            RefreshSliderSets(screen.sliderSets, idx => comp.GetSliderValue(idx));
        }

        public static void TryRefreshDispenserSideScreen(DispenserSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshSealedDoorSideScreen(SealedDoorSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshProgressBarSideScreen(ProgressBarSideScreen screen, GameObject go)
        {
            screen.RefreshBar();
        }

        public static void TryRefreshRailGunSideScreen(RailGunSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out RailGun gun)) return;
            screen.slider.value = gun.launchMass;
            screen.numberInput.SetDisplayValue(gun.launchMass.ToString());
            screen.UpdateHEPLabels();
        }

        public static void TryRefreshLogicBitSelectorSideScreen(LogicBitSelectorSideScreen screen, GameObject go)
        {
            screen.RefreshToggles();
        }

        public static void TryRefreshLogicBroadcastChannelSideScreen(LogicBroadcastChannelSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshClusterLocationFilterSideScreen(ClusterLocationFilterSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshGeneShufflerSideScreen(GeneShufflerSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshLureSideScreen(LureSideScreen screen, GameObject go)
        {
            screen.RefreshToggles();
        }

        public static void TryRefreshConfigureConsumerSideScreen(ConfigureConsumerSideScreen screen, GameObject go)
        {
            screen.RefreshToggles();
            screen.RefreshDetails();
        }

        public static void TryRefreshHighEnergyParticleDirectionSideScreen(HighEnergyParticleDirectionSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshClusterDestinationSideScreen(ClusterDestinationSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshCommandModuleSideScreen(CommandModuleSideScreen screen, GameObject go)
        {
            screen.RefreshConditions();
        }

        public static void TryRefreshSummonCrewSideScreen(SummonCrewSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshAssignPilotAndCrewSideScreen(AssignPilotAndCrewSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshRocketInteriorSectionSideScreen(RocketInteriorSectionSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshLaunchPadSideScreen(LaunchPadSideScreen screen, GameObject go)
        {
            screen.RefreshWaitingToLandList();
            screen.RefreshRocketButton();
        }

        public static void TryRefreshWarpPortalSideScreen(WarpPortalSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshSelfDestructButtonSideScreen(SelfDestructButtonSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshResearchSideScreen(ResearchSideScreen screen, GameObject go)
        {
            screen.RefreshDisplayState();
        }

        public static void TryRefreshTelescopeSideScreen(TelescopeSideScreen screen, GameObject go)
        {
            screen.RefreshDisplayState();
        }

        public static void TryRefreshConditionListSideScreen(ConditionListSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshMonumentSideScreen(MonumentSideScreen screen, GameObject go)
        {
            screen.GenerateStateButtons();
        }

        public static void TryRefreshRocketRestrictionSideScreen(RocketRestrictionSideScreen screen, GameObject go)
        {
            screen.UpdateButtonStates();
        }

        public static void TryRefreshButtonMenuSideScreen(ButtonMenuSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshNToggleSideScreen(NToggleSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshFewOptionSideScreen(FewOptionSideScreen screen, GameObject go)
        {
            screen.RefreshOptions();
        }

        public static void TryRefreshCargoModuleSideScreen(CargoModuleSideScreen screen, GameObject go)
        {
            screen.RefreshProgressBars();
        }

        public static void TryRefreshModuleFlightUtilitySideScreen(ModuleFlightUtilitySideScreen screen, GameObject go)
        {
            screen.RefreshAll();
        }

        public static void TryRefreshPrinterceptorSideScreen(PrinterceptorSideScreen screen, GameObject go)
        {
            screen.RefreshDisplay();
        }

        public static void TryRefreshBaseGameImpactorImperativeSideScreen(BaseGameImpactorImperativeSideScreen screen, GameObject go)
        {
            screen.Build();
        }

        public static void TryRefreshGeneticAnalysisStationSideScreen(GeneticAnalysisStationSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshArtifactAnalysisSideScreen(ArtifactAnalysisSideScreen screen, GameObject go)
        {
            screen.RefreshRows();
        }

        public static void TryRefreshLaunchButtonSideScreen(LaunchButtonSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshLoreBearerSideScreen(LoreBearerSideScreen screen, GameObject go)
        {
            screen.Refresh();
        }

        public static void TryRefreshOwnablesSidescreen(OwnablesSidescreen screen, GameObject go)
        {
            screen.RefreshSelectedStatusOnRows();
        }

        public static void TryRefreshPixelPackSideScreen(PixelPackSideScreen screen, GameObject go)
        {
            screen.PopulateColorSelections();
            screen.HighlightUsedColors();
        }

        public static void TryRefreshRocketModuleSideScreen(RocketModuleSideScreen screen, GameObject go)
        {
            screen.UpdateButtonStates();
        }

        // TODO: Implement
        public static void TryRefreshSingleItemSelectionSideScreen(SingleItemSelectionSideScreen screen, GameObject go)
        {
            //screen.SetData(screen.categories);
        }

        // TODO: Implement
        public static void TryRefreshSpecialCargoBayClusterSideScreen(SpecialCargoBayClusterSideScreen screen, GameObject go)
        {
            //screen.UpdateState(null);
        }

        public static void TryRefreshTagFilterScreen(TagFilterScreen screen, GameObject go)
        {
            screen.Filter(screen.acceptedTags);
        }

        // Needs proper testing
        public static void TryRefreshTelepadSideScreen(TelepadSideScreen screen, GameObject go)
        {
            //screen.UpdateVictoryConditions();
            //screen.UpdateAchievementsUnlocked();
            //screen.UpdateSkills();

            //screen.BuildVictoryConditions();
        }

        public static void TryRefreshTemporalTearSideScreen(TemporalTearSideScreen screen, GameObject go)
        {
            screen.RefreshPanel();
        }

        public static void TryRefreshBionicSideScreen(BionicSideScreen screen, GameObject go)
        {
            screen.RecreateBionicSlots();
        }

        public static void TryRefreshAutoPlumberSideScreen(AutoPlumberSideScreen screen, GameObject go)
        {

        }

        public static void TryRefreshClusterGridWorldSideScreen(ClusterGridWorldSideScreen screen, GameObject go)
        {
            if (!go.TryGetComponent(out AsteroidGridEntity entity)) return;
            screen.icon.sprite = Def.GetUISprite(entity).first;
            WorldContainer world = entity.GetComponent<WorldContainer>();
            bool discovered = world != null && world.IsDiscovered;
            screen.viewButton.isInteractable = discovered;
            screen.viewButton.GetComponent<ToolTip>().SetSimpleTooltip(
                discovered ? global::STRINGS.UI.UISIDESCREENS.CLUSTERWORLDSIDESCREEN.VIEW_WORLD_TOOLTIP
                           : global::STRINGS.UI.UISIDESCREENS.CLUSTERWORLDSIDESCREEN.VIEW_WORLD_DISABLE_TOOLTIP);
        }

        public static void TryRefreshHabitatModuleSideScreen(HabitatModuleSideScreen screen, GameObject go)
        {
        }

        public static void TryRefreshNoConfigSideScreen(NoConfigSideScreen screen, GameObject go)
        {
        }

        public static void TryRefreshRoleStationSideScreen(RoleStationSideScreen screen, GameObject go)
        {
        }
    }
}
