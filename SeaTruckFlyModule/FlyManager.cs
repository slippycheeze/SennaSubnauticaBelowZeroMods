﻿extern alias SEZero;

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UWE;
using BZCommon;
using BZCommon.Helpers;
using System;
using SEZero::SlotExtenderZero.API;

namespace SeaTruckFlyModule
{
    public partial class FlyManager : MonoBehaviour
    {
        enum TruckState
        {
            None,
            Diving,
            TakeOff,
            Flying,
            AutoFly,
            Landing,
            Landed            
        };

        enum TruckPosition
        {
            None,
            BelowWater,
            AboveWater,
            AboveSurface,
            NearSurface,
            OnSurface
        };
        
        public SeaTruckHelper helper = null;

        private ObjectHelper objectHelper = new ObjectHelper();

        public SeaTruckMotor motor;

        public bool isEnabled = false;

        private bool isFirstCheckComplete = false;
        
        private bool isGraphicsComplete = false;

        public Utils.MonitoredValue<bool> isFlying = new Utils.MonitoredValue<bool>();        

        private FMOD_CustomLoopingEmitter engineSound;

        private FMODAsset engine;

        private FMODAsset engineDefault;              

        public float distanceFromSurface, FLeftDist, FRightDist;

        public float altitude;

        public static readonly Vector3 LeftDown = new Vector3(-1f, -1f, 0f);
        public static readonly Vector3 RightDown = new Vector3(1f, -1f, 0f);

        private TruckState _seatruckState = TruckState.None;
        private TruckState SeatruckState
        {
            get
            {
                return _seatruckState;
            }
            set
            {
                if (_seatruckState != value)
                {
                    _seatruckState = value;

                    OnSeatruckStateChanged();
                }
            }
        }

        private TruckPosition _seatruckPosition = TruckPosition.None;
        private TruckPosition SeatruckPosition
        {
            get
            {
                return _seatruckPosition;
            }
            set
            {
                if (_seatruckPosition != value)
                {
                    _seatruckPosition = value;

                    OnSeatruckPositionChanged();                    
                }               
            }
        }

        private void OnSeatruckStateChanged()
        {
            switch (SeatruckState)
            {
                case TruckState.Flying:
                    helper.TruckWorldForces.aboveWaterGravity = 0f;
                    break;
                case TruckState.Landing:
                    helper.TruckWorldForces.aboveWaterGravity = 9.81f;
                    break;
            }
#if DEBUG            
            UpdateSeatruckState();
#endif
        }

        private void OnSeatruckPositionChanged()
        {
            switch (SeatruckPosition)
            {
                case TruckPosition.AboveWater:
                case TruckPosition.AboveSurface:
                    if (!isFlying.value)
                    {
                        isFlying.Update(true);
                    }
                    break;

                case TruckPosition.BelowWater:
                    if (isFlying.value)
                    {
                        isFlying.Update(false);
                    }
                    break;
            }
#if DEBUG
            UpdateSeatruckPosition();
#endif
        }        

        private GameObject mainCab;

        private Vector3 mainCabExitPoint;

        private readonly Vector3 mainCabNewExitPoint1 = new Vector3(0.0f, -0.80f, -1.0f);

        private readonly Vector3 mainCabNewExitPoint2 = new Vector3(1.78f, -0.80f, 2.0f);

        float timer = 0.0f;        

        Vector3 jumpUp = new Vector3(0, 20, 0);
        Vector3 jumpDown = new Vector3(0, -10, 0);

        public void Awake()
        {
            //helper = new SeaTruckHelper(gameObject, false, false, false);
            helper = SeatruckServices.Main.GetSeaTruckHelper(gameObject);

            mainCab = helper.MainCab;
            motor = helper.TruckMotor;
            engineSound = motor.engineSound;
            rigidbody = helper.TruckWorldForces.useRigidbody;

            CoroutineHost.StartCoroutine(LoadBiodomeRobotArmResourcesAsync());

            CoroutineHost.StartCoroutine(Init_Graphics());              

            engineDefault = ScriptableObject.CreateInstance<FMODAsset>();
            engineDefault.name = "engine";
            engineDefault.path = "event:/bz/vehicles/seatruck/engine";

            engine = ScriptableObject.CreateInstance<FMODAsset>();
            engine.name = "engine";
            engine.path = "event:/sub/drone/motor_loop";                                               

            isFlying.changedEvent.AddHandler(this, new Event<Utils.MonitoredValue<bool>>.HandleFunction(OnFlyModeChanged));

            mainCabExitPoint = helper.TruckSegment.exitPosition.localPosition;

            //helper.onPilotingBegin += OnPilotingBegin;

#if DEBUG
            CoroutineHost.StartCoroutine(InitDebugHUD());
#endif
        }

        /*
        private void OnPilotingBegin()
        {
            print("OnPilotingBegin triggered!");

            if (SeatruckState == TruckState.Landed && SeatruckPosition == TruckPosition.OnSurface)
            {
                rigidbody.isKinematic = true;
            }
        }
        

        private void OnDestroy()
        {
            helper.onPilotingBegin -= OnPilotingBegin;
        }
        */

        private void OnFlyModeChanged(Utils.MonitoredValue<bool> newValue)
        {            
            if (newValue.value)
            {
                SetHandTargets(false);

                engineSound.Stop();
                engineSound.asset = engine;

                //helper.TruckWorldForces.aboveWaterGravity = 0f;                
                rigidbody.drag = 1f;

                //ErrorMessage.AddDebug("Seatruck has take off");                
            }
            else
            {
                engineSound.Stop();
                engineSound.asset = engineDefault;
                                
                if (SeatruckPosition == TruckPosition.BelowWater)
                {
                    SetLandingFoots(false);
                    SetHandTargets(true);
                    SetExitPosition();                    
                }                
            }           
        }        

        private void SetLandingFoots(bool value)
        {
            landingFoots.SetActive(value);
            landingFootCollisions.SetActive(value);

            if (value)
            {
                SetRearFoots();
                ErrorMessage.AddDebug("Landing foots extended");
            }
            else
            {
                ErrorMessage.AddDebug("Landing foots retracted");
            }
        }
        

        private void SetExitPosition()
        {
            if (SeatruckState == TruckState.Landed && SeatruckPosition == TruckPosition.OnSurface)
            {
                if (!helper.IsSeatruckChained())
                {
                    helper.TruckSegment.exitPosition.localPosition = mainCabNewExitPoint1;
                }
                else
                {
                    helper.TruckSegment.exitPosition.localPosition = mainCabNewExitPoint2;
                }
            }
            else
            {
                helper.TruckSegment.exitPosition.localPosition = mainCabExitPoint;
            }
        }

        
        
        

        /*
        public void Update()
        {
            if (!isFirstCheckComplete && helper != null)
            {
                CheckSlotsForFlyModule();
                isFirstCheckComplete = true;
            }            
        }
        */

        public void CheckSlotsForFlyModule()
        {
            foreach (string slot in helper.TruckSlotIDs)
            {
                TechType techType = helper.TruckEquipment.GetTechTypeInSlot(slot);

                if (techType == SeaTruckFlyModule_Prefab.TechTypeID)
                {
                    isEnabled = true;

                    if (helper.MainCab.transform.position.y > 0)
                    {
                        if (rigidbody.velocity == Vector3.zero)
                        {
                            if (SeatruckState != TruckState.Landed)
                            {
                                SeatruckState = TruckState.Landed;
                                SeatruckPosition = TruckPosition.OnSurface;
                                rigidbody.isKinematic = true;

                                SetLandingFoots(true);
                                SetHandTargets(false);
                                SetExitPosition();
                            }
                        }
                    }

                    //UpdateDebugHUD();

                    break;
                }
                else
                {                                        
                    isEnabled = false;
                    helper.TruckWorldForces.aboveWaterGravity = 9.81f;
                }
            }

            ErrorMessage.AddDebug($"Seatruck fly mode {(isEnabled ? "enabled" : "disabled")}");
        }
                

        void Update()
        {
            if (!isFirstCheckComplete && helper != null)
            {
                CheckSlotsForFlyModule();
                isFirstCheckComplete = true;
            }

            if (!isEnabled)
            {
                return;
            }

            if (helper == null || motor == null/* || !helper.IsPiloted()*/)
                return;
            
            if (!helper.IsPiloted() && SeatruckPosition == TruckPosition.OnSurface && SeatruckState == TruckState.Landed && !rigidbody.isKinematic)
            {
                rigidbody.isKinematic = true;                
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                StartCoroutine(SeatruckCallBack());
            }
            
            if (!helper.IsPiloted() && SeatruckState == TruckState.Landed)
                return;            

            altitude = helper.MainCab.transform.position.y;
                        
            altitudeMeter.transform.localRotation = Quaternion.AngleAxis(360 - mainCab.transform.eulerAngles.x, Vector3.right);

            if (Physics.Raycast(altitudeMeter.transform.position, altitudeMeter.transform.TransformDirection(Vector3.down), out RaycastHit raycastDown, 100f, -1, QueryTriggerInteraction.Ignore))
            {
                GameObject gameObject = raycastDown.collider.gameObject;

                if (gameObject != null && gameObject.GetComponent<LiveMixin>() == null)
                {
                    distanceFromSurface = (altitude - raycastDown.point.y) - 3;                    
                }
                else
                {
                    distanceFromSurface = altitude;
                }                
            }
            else
            {
                distanceFromSurface = altitude;
            }

            if (Physics.Raycast(altitudeMeter.transform.position, altitudeMeter.transform.TransformDirection(LeftDown), out RaycastHit raycastLeft, 100f, -1, QueryTriggerInteraction.Ignore))
            {
                GameObject gameObject = raycastLeft.collider.gameObject;

                if (gameObject != null && gameObject.GetComponent<LiveMixin>() == null)
                {
                    FLeftDist = (altitude - raycastLeft.point.y) - 3;
                }
                
            }
            else
            {
                FLeftDist = altitude;
            }

            if (Physics.Raycast(altitudeMeter.transform.position, altitudeMeter.transform.TransformDirection(RightDown), out RaycastHit raycastRight, 100f, -1, QueryTriggerInteraction.Ignore))
            {
                GameObject gameObject = raycastRight.collider.gameObject;

                if (gameObject != null && gameObject.GetComponent<LiveMixin>() == null)
                {
                    FRightDist = (altitude - raycastRight.point.y) - 3;
                }

            }
            else
            {
                FRightDist = altitude;
            }
            //prevseatruckPosition = SeatruckPosition;
            //prevflyState = SeatruckState;


            if (SeatruckState != TruckState.Landing || SeatruckState != TruckState.TakeOff)
            {
                if (altitude < 0)
                {
                    SeatruckPosition = TruckPosition.BelowWater;
                    SeatruckState = TruckState.Diving;
                }
                else if (altitude > 0 && altitude <= distanceFromSurface)
                {
                    SeatruckPosition = TruckPosition.AboveWater;
                    SeatruckState = TruckState.Flying;
                }
                else if (altitude > distanceFromSurface && distanceFromSurface > 20.0f)
                {
                    SeatruckPosition = TruckPosition.AboveSurface;
                    SeatruckState = TruckState.Flying;
                }
                else if (altitude > 0 && distanceFromSurface > 1.0f && distanceFromSurface < 15.0f )
                {
                    SeatruckPosition = TruckPosition.NearSurface;
                    SeatruckState = TruckState.Flying;
                }                
            }

            if (altitude > 0)
            {
                hudTextAltitude.text = $"{(int)altitude}";                
            }

            if (Input.GetKeyDown(KeyCode.L) && SeatruckPosition == TruckPosition.NearSurface)
            {
                if (!CheckLandingSurface())
                {
                    ErrorMessage.AddDebug("This landing surface not safe!");
                    return;
                }

                /*
                if (!landingFoots.activeSelf)
                {
                    SetLandingFoots(true);
                }

                ErrorMessage.AddDebug("Landing sequence started");
                engineSound.Stop();

                //helper.thisWorldForces.aboveWaterGravity = 9.81f;
                timer = 0.0f;
                */

                StartCoroutine(OnLanding(new Vector3(0, -distanceFromSurface, 0)));
               
                //ErrorMessage.AddDebug("Seatruck cannot land on this position");                
            }

            

#if DEBUG
                UpdateDebugHUD();
#endif
        }
        
        /*
        private void SetWarningText(string text, bool value)
        {
            bool isActive = WarningTextArea.activeSelf;
            warningText.text = text;

            if (value == isActive)
            {
                return;
            }
            else
            {
                WarningTextArea.SetActive(value);
            }
            
        }
        */

        private void SetRearFoots()
        {
            if (helper.IsSeatruckChained())
            {
                float zShift = helper.GetSeatruckZShift();

                hatchTriggerCloneRear.SetActive(false);

                hatchTriggerCloneFront.SetActive(true);

                rearFoots.transform.localPosition = new Vector3(0, 0, zShift);

                rearFoots.SetActive(true);
                
                rearFootCollisions.transform.localPosition = new Vector3(0, 0, zShift);

                rearFootCollisions.SetActive(true);
            }
            else
            {
                rearFoots.SetActive(false);

                rearFootCollisions.SetActive(false);

                hatchTriggerCloneRear.SetActive(true);

                hatchTriggerCloneFront.SetActive(false);
            }
        }


        private void SetHandTargets(bool value)
        {
            detachLever.SetActive(value);

            if (!helper.IsSeatruckChained())
            {
                return;
            }

            List<GameObject> wheelTriggers = helper.GetWheelTriggers();
            
            foreach (GameObject wheelTrigger in wheelTriggers)
            {
                wheelTrigger.SetActive(value);
            }

            ErrorMessage.AddDebug($"Detach lever and module handtargets {(value ? "enabled" : "disabled")}");
        }


        private bool CheckLandingSurface()
        {
            return ((distanceFromSurface + FLeftDist + FRightDist) / 3) <= distanceFromSurface * 1.2f;
        }

        


    }
}
