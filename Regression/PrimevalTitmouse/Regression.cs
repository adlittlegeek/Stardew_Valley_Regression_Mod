﻿using Microsoft.Xna.Framework;
using Regression;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;
using System;
using System.Collections.Generic;

namespace PrimevalTitmouse
{
    public class Regression : Mod
    {
        public static int lastTimeOfDay = 0;
        public static bool morningHandled = true;
        public static Random rnd = new Random();
        public static bool started = false;
        public Body body;
        public static Config config;
        public static IModHelper help;
        public static IMonitor monitor;
        public bool shiftHeld;
        public static Data t;
        public static Farmer who;
        public static readonly List<string> beverages = new() { "Cola", "Espresso", "Coffee", "Wine", "Beer", "Milk", "Tea", "Juice" };

        const float timeInTick = 0.003100775f;
        public override void Entry(IModHelper h)
        {
            help = h;
            monitor = Monitor;
            config = Helper.ReadConfig<Config>();
            t = Helper.Data.ReadJsonFile<Data>(string.Format("{0}.json", (object)config.Lang)) ?? Helper.Data.ReadJsonFile<Data>("en.json");
            h.Events.GameLoop.Saving += new EventHandler<SavingEventArgs>(this.BeforeSave);
            h.Events.GameLoop.DayStarted += new EventHandler<DayStartedEventArgs>(ReceiveAfterDayStarted);
            h.Events.GameLoop.UpdateTicked += new EventHandler<UpdateTickedEventArgs>(ReceiveEighthUpdateTick);
            h.Events.GameLoop.TimeChanged += new EventHandler<TimeChangedEventArgs>(ReceiveTimeOfDayChanged);
            h.Events.Input.ButtonPressed += new EventHandler<ButtonPressedEventArgs>(ReceiveKeyPress);
            h.Events.Input.ButtonPressed += new EventHandler<ButtonPressedEventArgs>(ReceiveMouseChanged);
            h.Events.Display.MenuChanged += new EventHandler<MenuChangedEventArgs>(ReceiveMenuChanged);
            h.Events.Display.RenderingHud += new EventHandler<RenderingHudEventArgs>(ReceivePreRenderHudEvent);
        }

        public void DrawStatusBars()
        {
            int x1 = Game1.viewport.Width - (65 + StatusBars.barWidth);
            int y1 = Game1.viewport.Height - (25 + StatusBars.barHeight);
            if (Game1.currentLocation is MineShaft || Game1.currentLocation is Woods || Game1.currentLocation is SlimeHutch || who.health < who.maxHealth)
                x1 -= 58;
            if (!config.NoHungerAndThirst || PrimevalTitmouse.Regression.config.Debug)
            {
                float percentage1 = this.body.food / body.maxFood;
                StatusBars.DrawStatusBar(x1, y1, percentage1, new Color(115, byte.MaxValue, 56));
                int x2 = x1 - (10 + StatusBars.barWidth);
                float percentage2 = body.water / body.maxWater;
                StatusBars.DrawStatusBar(x2, y1, percentage2, new Color(117, 225, byte.MaxValue));
                x1 = x2 - (10 + StatusBars.barWidth);
            }
            if (config.Debug)
            {
                if (config.Messing)
                {
                    float percentage = body.bowels / body.maxBowels;
                    StatusBars.DrawStatusBar(x1, y1, percentage, new Color(146, 111, 91));
                    x1 -= 10 + StatusBars.barWidth;
                }
                if (config.Wetting)
                {
                    float percentage = body.bladder / body.maxBladder;
                    StatusBars.DrawStatusBar(x1, y1, percentage, new Color(byte.MaxValue, 225, 56));
                }
            }
            if (!config.Wetting && !config.Messing)
                return;
            int y2 = (Game1.player.questLog).Count == 0 ? 250 : 310;
            Animations.DrawUnderwearIcon(body.underwear, Game1.viewport.Width - 94, y2);
        }

        private void GiveUnderwear()
        {
            List<Item> objList = new List<Item>();
            foreach (string validUnderwearType in Strings.ValidUnderwearTypes())
                objList.Add(new Underwear(validUnderwearType, 0.0f, 0.0f, 20));
            objList.Add(new StardewValley.Object(399, 99, false, -1, 0));
            objList.Add(new StardewValley.Object(348, 99, false, -1, 0));
            Game1.activeClickableMenu = new ItemGrabMenu(objList);
        }

        private void ReceiveAfterDayStarted(object sender, DayStartedEventArgs e)
        {
            body = Helper.Data.ReadJsonFile<Body>(string.Format("{0}/RegressionSave.json", Constants.SaveFolderName)) ?? new Body();
            started = true;
            who = Game1.player;
            morningHandled = false;
            Animations.AnimateNight(body);
        }

        //Save Mod related variables in separate JSON. Also trigger night handling if not on the very first day.
        private void BeforeSave(object Sender, SavingEventArgs e)
        {
            body.bedtime = lastTimeOfDay;
            if (Game1.dayOfMonth != 1 || Game1.currentSeason != "spring" || Game1.year != 1)
                body.HandleNight();
            if (string.IsNullOrWhiteSpace(Constants.SaveFolderName))
                return;

            Helper.Data.WriteJsonFile(string.Format("{0}/RegressionSave.json", Constants.SaveFolderName), body);
        }



        private void ReceiveEighthUpdateTick(object sender, UpdateTickedEventArgs e)
        {
            //Only act on every eigth tick (should this take multiplayer into account?)
            if (e.IsMultipleOf(8))
            {
                //Ignore everything until we've started the day
                if (!started)
                    return;

                //If we haven't performed our morning actions, do so.
                if (!morningHandled && !Game1.fadeToBlack && who.canMove)
                {
                    body.HandleMorning();
                    morningHandled = true; //Make sure we do his only once per day
                }

                //If time is moving, update our body state (Hunger, thirst, etc.)
                if (ShouldTimePass())
                    this.body.HandleTime(timeInTick);

                //Handle eating and drinking.
                if (Game1.player.isEating && Game1.activeClickableMenu == null)
                {
                    if (beverages.Contains(who.itemToEat.Name))
                        body.DrinkBeverage();
                    else
                        body.Eat();
                }
            }
        }

        //Determine if we need to handle time passing (not the same as Game time passing)
        private static bool ShouldTimePass()
        {
            return ((Game1.game1.IsActive || Game1.options.pauseWhenOutOfFocus == false) && (Game1.paused == false && Game1.dialogueUp == false) && (Game1.currentMinigame == null && Game1.eventUp == false && (Game1.activeClickableMenu == null && Game1.menuUp == false)) && Game1.fadeToBlack == false);
        }

        //Interprete key-presses
        private void ReceiveKeyPress(object sender, ButtonPressedEventArgs e)
        {
            //If we haven't started the day, ignore the key presses
            if (!started)
                return;

            //Interpret buttons differently if holding Left Alt & Debug is enabled
            if (e.IsDown(SButton.LeftAlt) && config.Debug)
            {
                switch (e.Button)
                {
                    case SButton.F1: //
                            body.DecreaseFoodAndWater();
                            break;
                    case SButton.F2: //
                            body.IncreaseEverything();
                            break;
                    case SButton.F3://
                            GiveUnderwear();
                            break;
                    case SButton.F5://Alt F4 is reserved to close
                            TimeMagic.doMagic();
                            break;
                    case SButton.F6:
                        config.Wetting = !config.Wetting;
                        break;
                    case SButton.F7:
                        config.Messing = !config.Messing;
                        break;
                    case SButton.F8:
                        config.Easymode = !config.Easymode;
                        break;
                }
            }
            else
            {
                switch (e.Button)
                {
                    case SButton.F1:
                        if (!body.IsOccupied())
                        {
                            body.StartWetting(true, !e.IsDown(SButton.LeftShift));
                            break;
                        }
                        break;
                    case SButton.F2:
                        if (!body.IsOccupied())
                        {
                            body.StartMessing(true, !e.IsDown(SButton.LeftShift));
                            break;
                        }
                        break;
                    case SButton.F5:
                        Animations.CheckUnderwear(body);
                        break;
                    case SButton.F6: /*F4 is reserved for screenshot mode*/
                        Animations.CheckPants(body);
                        break;
                    case SButton.F9:
                        config.Debug = !config.Debug;
                        break;
                }
            }
        }

        //A menu has been opened, figure out if we need to modify it
        private void ReceiveMenuChanged(object sender, MenuChangedEventArgs e)
        {
            //Don't do anything if our day hasn't started
            if (!started)
                return;

            DialogueBox attemptToSleepMenu;
            ShopMenu currentShopMenu;

            //If we try to sleep, check if the bed is done drying (only matters in Hard Mode)
            if (Game1.currentLocation is FarmHouse && (attemptToSleepMenu = e.NewMenu as DialogueBox) != null && Game1.currentLocation.lastQuestionKey == "Sleep" && !config.Easymode)
            {
                //If enough time has passed, the bed has dried
                if (body.beddingDryTime > Game1.timeOfDay)
                {
                    List<Response> sleepAttemptResponses = attemptToSleepMenu.responses;
                    if (sleepAttemptResponses.Count == 2)
                    {
                        Response response = sleepAttemptResponses[1];
                        Game1.currentLocation.answerDialogue(response);
                        Game1.currentLocation.lastQuestionKey = null;
                        attemptToSleepMenu.closeDialogue();
                        Animations.AnimateDryingBedding(body);
                    }
                }
            }
            //If we're in the mailbox, handle the initial letter from Jodi that contains protection
            else if (e.NewMenu is LetterViewerMenu && Game1.currentLocation is Farm)
            {
                LetterViewerMenu letterMenu = (LetterViewerMenu)e.NewMenu;
                Mail.ShowLetter(letterMenu);
            }
            //If we're trying to shop, handle the underwear inventory
            else if((currentShopMenu = e.NewMenu as ShopMenu) != null)
            {
                //Default to all underwear being available
                List<string> allUnderwear = Strings.ValidUnderwearTypes();
                List<string> availableUnderwear = allUnderwear;
                bool underwearAvailableAtShop = false;
                if(Game1.currentLocation is SeedShop)
                {
                    //The seed shop does not sell the Joja diaper
                    availableUnderwear.Remove("Joja diaper");
                    underwearAvailableAtShop = true;
                } else if(Game1.currentLocation is JojaMart)
                {
                    //Joja shop ONLY sels the Joja diaper and a cloth diaper
                    availableUnderwear.Clear();
                    availableUnderwear.Add("Joja diaper");
                    availableUnderwear.Add("Cloth diaper");
                    underwearAvailableAtShop = true;
                }

                if(underwearAvailableAtShop)
                {
                    foreach(string type in availableUnderwear)
                    {
                        Underwear underwear = new Underwear(type, 0.0f, 0.0f, 1);
                        int[] priceAndQty = new int[2] {underwear.container.price, 999};
                        currentShopMenu.forSale.Add(underwear);
                        currentShopMenu.itemPriceAndStock.Add(underwear, priceAndQty);
                    }
                }
            }
        }

        //Check if we are at a natural water source
        private static bool AtWaterSource()
        {
            GameLocation currentLocation = Game1.currentLocation;
            Vector2 toolLocation = who.GetToolLocation(false);
            int x = (int)toolLocation.X;
            int y = (int)toolLocation.Y;
            return currentLocation.doesTileHaveProperty(x / Game1.tileSize, y / Game1.tileSize, "Water", "Back") != null || currentLocation.doesTileHaveProperty(x / Game1.tileSize, y / Game1.tileSize, "WaterSource", "Back") != null;
        }

        //Check if we are at the Well (and its constructed)
        private static bool AtWell()
        {
            GameLocation currentLocation = Game1.currentLocation;
            Vector2 toolLocation = who.GetToolLocation(false);
            int x = (int)toolLocation.X;
            int y = (int)toolLocation.Y;
            Vector2 vector2 = new Vector2((float)(x / Game1.tileSize), y / Game1.tileSize);
            return currentLocation is BuildableGameLocation && (currentLocation as BuildableGameLocation).getBuildingAt(vector2) != null && ((currentLocation as BuildableGameLocation).getBuildingAt(vector2).buildingType.Value.Equals("Well") && (currentLocation as BuildableGameLocation).getBuildingAt(vector2).daysOfConstructionLeft.Value <= 0);

        }

        //Handle Mouse Clicks/Movement
        private void ReceiveMouseChanged(object sender, ButtonPressedEventArgs e)
        {
            //Ignore if we aren't started or otherwise paused
            if (!(Game1.game1.IsActive && !Game1.paused && started))
            {
                return;
            }

            //Handle a Right Click
            if (e.Button == SButton.MouseRight)
            {
                //If Right click is already being interpreted by another event (or we otherwise wouldn't process such an event. Ignore it.
                if ((Game1.dialogueUp || Game1.currentMinigame != null || (Game1.eventUp || Game1.activeClickableMenu != null) || Game1.menuUp || Game1.fadeToBlack) || (who.isRidingHorse() || !who.canMove || (Game1.player.isEating || who.canOnlyWalk) || who.FarmerSprite.pauseForSingleAnimation))
                    return;

                ////If we're holding the watering can, attempt to drink from it.
                /////This is the highest priority (apparently?)
                if (who.CurrentTool != null && who.CurrentTool is WateringCan && e.IsDown(SButton.LeftShift))
                {
                    this.body.DrinkWateringCan();
                    return;
                }

                //Otherwise Check if we're holding underwear
                Underwear activeObject = who.ActiveObject as Underwear;
                if (activeObject != null)
                {
                    //If the Underwear we are holding isn't currently wet, messy, or drying; change into it.
                    if ((double)activeObject.container.wetness + (double)activeObject.container.messiness == 0.0 && !activeObject.container.drying)
                    {
                        who.reduceActiveItemByOne(); //Take it out of inventory
                        Container container = body.ChangeUnderwear(activeObject); //Put on the new underwear and return the old
                        Underwear underwear = new Underwear(container.name, container.wetness, container.messiness, 1);

                        //Try to put the old underwear into the inventory, but pull up the management window if it can't fit
                        if (!who.addItemToInventoryBool(underwear, false))
                        {
                            List<Item> objList = new List<Item>();
                            objList.Add(underwear);
                            Game1.activeClickableMenu = new ItemGrabMenu(objList);
                        }
                    }
                    //If it is wet, messy or drying, check if we can wash it
                    else if (activeObject.container.washable)
                    {
                        //Are we at a water source? If so, wash the underwear.
                        if (AtWaterSource())
                        {
                            Animations.AnimateWashingUnderwear(activeObject.container);
                            activeObject.container.wetness = 0.0f;
                            activeObject.container.messiness = 0.0f;
                            activeObject.container.drying = true;
                        }
                    }
                    return; //Done with underwear
                }
                    
                    
                //If we're at a water source, and not holding underwear, drink from it.
                if ((AtWaterSource()|| AtWell()) && e.IsDown(SButton.LeftShift))
                  this.body.DrinkWaterSource();
            }
                
        }

        //If approppriate, draw bars for Hunger, thirst, bladder and bowels
        public void ReceivePreRenderHudEvent(object sender, RenderingHudEventArgs args)
        {
            if (!started || Game1.currentMinigame != null || Game1.eventUp || Game1.globalFade)
                return;
            DrawStatusBars();
        }

        private void ReceiveTimeOfDayChanged(object sender, TimeChangedEventArgs e)
        {
            lastTimeOfDay = Game1.timeOfDay;

            //If its 6:10AM, handle delivering mail
            if (Game1.timeOfDay == 610)
                Mail.CheckMail();

            //If its earlier than 6:30, we aren't wet/messy don't notice that we're still soiled (or don't notice with ~5% chance even if soiled)
            if (rnd.NextDouble() >= 0.0555555559694767 || body.underwear.wetness + (double)body.underwear.messiness <= 0.0 || Game1.timeOfDay < 630)
                return;
            Animations.AnimateStillSoiled(this.body);
        }

        public Regression()
        {
            //base.Actor();
        }
    }
}