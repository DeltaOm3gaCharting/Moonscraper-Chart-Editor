﻿using UnityEngine;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.IO;

public class ClipboardObjectController : Snapable {
    const string CLIPBOARD_FILE_LOCATION = "/MoonscraperClipboard.bin";

    public GroupSelect groupSelectTool;
    public Transform strikeline;
    public static Clipboard clipboard = new Clipboard();
    Renderer ren;

    uint pastePos = 0;

    protected override void Awake()
    {
        base.Awake();
        ren = GetComponent<Renderer>();
    }

    new void LateUpdate()
    {
        if (Globals.applicationMode == Globals.ApplicationMode.Editor)
            ren.enabled = true;
        else
            ren.enabled = false;

        if (Mouse.world2DPosition != null && Input.mousePosition.y < Camera.main.WorldToScreenPoint(editor.mouseYMaxLimit.position).y)
        {
            pastePos = objectSnappedChartPos;
        }
        else
        {
            pastePos = editor.currentSong.WorldPositionToSnappedChartPosition(strikeline.position.y, Globals.step);
        }

        transform.position = new Vector3(transform.position.x, editor.currentSong.ChartPositionToWorldYPosition(pastePos), transform.position.z);

        if (Globals.modifierInputActive)
        {
            if (Input.GetKeyDown(KeyCode.V))
            {
                Paste(pastePos);
                groupSelectTool.reset();
            }
        }
    }

    public static void SetData(SongObject[] data, Clipboard.SelectionArea area, Song song)
    {
        clipboard = new Clipboard();
        clipboard.data = data;
        clipboard.SetCollisionArea(area, song);
        System.Windows.Forms.Clipboard.Clear();     // Clear the clipboard to mimic the real clipboard. For some reason putting custom objects on the clipboard with this dll doesn't work.

        FileStream fs = new FileStream(UnityEngine.Application.persistentDataPath + CLIPBOARD_FILE_LOCATION, FileMode.Create);

        BinaryFormatter formatter = new BinaryFormatter();
        try
        {
            formatter.Serialize(fs, clipboard);
        }
        catch (SerializationException e)
        {
            Debug.LogError("Failed to serialize. Reason: " + e.Message);
        }
        finally
        {
            fs.Close();
        }
    }

    // Paste the clipboard data into the chart, overwriting anything else in the process
    public void Paste(uint chartLocationToPaste)
    {
        if (System.Windows.Forms.Clipboard.GetDataObject().GetFormats().Length > 0)     // Something else is pasted on the clipboard instead of Moonscraper stuff.
            return;

        FileStream fs = null;
        clipboard = null;
        try
        {
            // Read clipboard data from a file instead of the actual clipboard because the actual clipboard doesn't work for whatever reason
            fs = new FileStream(UnityEngine.Application.persistentDataPath + CLIPBOARD_FILE_LOCATION, FileMode.Open);
            BinaryFormatter formatter = new BinaryFormatter();

            clipboard = (Clipboard)formatter.Deserialize(fs);
        }
        catch
        {
            Debug.LogError("Failed to read from clipboard file");
        }
        finally
        {
            if (fs != null)
                fs.Close();
        }

        if (Globals.applicationMode == Globals.ApplicationMode.Editor && clipboard != null && clipboard.data.Length > 0)
        {
            List<ActionHistory.Action> record = new List<ActionHistory.Action>();
            Rect collisionRect = clipboard.GetCollisionRect(chartLocationToPaste, editor.currentSong);
            uint colliderChartDistance = clipboard.areaChartPosMax - clipboard.areaChartPosMin;

            // Overwrite any objects in the clipboard space
            if (clipboard.data[0].GetType().IsSubclassOf(typeof(ChartObject)))
            {
                foreach (ChartObject chartObject in editor.currentChart.chartObjects)
                {
                    if (chartObject.position >= chartLocationToPaste && chartObject.position < chartLocationToPaste + colliderChartDistance && PrefabGlobals.HorizontalCollisionCheck(PrefabGlobals.GetCollisionRect(chartObject), collisionRect))
                    {
                        chartObject.Delete(false);

                        record.Add(new ActionHistory.Delete(chartObject));
                    }
                }
            }
            else
            {
                // Overwrite synctrack, leave sections alone
                foreach (SyncTrack syncObject in editor.currentSong.syncTrack)
                {
                    if (syncObject.position >= chartLocationToPaste && syncObject.position < chartLocationToPaste + colliderChartDistance && PrefabGlobals.HorizontalCollisionCheck(PrefabGlobals.GetCollisionRect(syncObject), collisionRect))
                    {
                        syncObject.Delete(false);

                        record.Add(new ActionHistory.Delete(syncObject));
                    }
                }
            }     

            // Paste the new objects in
            foreach (SongObject clipboardSongObject in clipboard.data)
            {
                SongObject objectToAdd = clipboardSongObject.Clone();

                objectToAdd.position = chartLocationToPaste + clipboardSongObject.position - clipboard.areaChartPosMin;

                if (objectToAdd.GetType() == typeof(Note))
                {
                    record.AddRange(PlaceNote.AddObjectToCurrentChart((Note)objectToAdd, editor, false));
                }
                else if (objectToAdd.GetType() == typeof(Starpower))
                {
                    record.AddRange(PlaceStarpower.AddObjectToCurrentChart((Starpower)objectToAdd, editor, false));
                }
                else
                {
                    PlaceSongObject.AddObjectToCurrentEditor(objectToAdd, editor, false);

                    record.Add(new ActionHistory.Add(objectToAdd));
                }
                
            }
            editor.currentChart.updateArrays();
            editor.currentSong.updateArrays();
            editor.actionHistory.Insert(record.ToArray());
        }
        // 0 objects in clipboard, don't bother pasting
    }
}
