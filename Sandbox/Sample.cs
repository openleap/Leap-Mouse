/******************************************************************************\
* Copyright (C) 2012-2013 Leap Motion, Inc. All rights reserved.               *
* Leap Motion proprietary and confidential. Not for distribution.              *
* Use subject to the terms of the Leap Motion SDK Agreement available at       *
* https://developer.leapmotion.com/sdk_agreement, or another agreement         *
* between Leap Motion and you, your company or other organization.             *
\******************************************************************************/

using System;
using System.Threading;
using Leap;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

class SampleListener : Listener
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, UIntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP = 0x04;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const uint MOUSEEVENTF_RIGHTUP = 0x10;

    private Object thisLock = new Object();

    private Queue<Vector> positions = new Queue<Vector>();
    private Queue<Vector> velocities = new Queue<Vector>();

    private Queue<Vector> thumbDistances = new Queue<Vector>();
    private Queue<Vector> thumbVelocities = new Queue<Vector>();

    private bool thumbDown;

    private void SafeWriteLine(String line)
    {
        lock (thisLock)
        {
            Console.WriteLine(line);
        }
    }

    private void sendMouseDown()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
    }

    private void sendMouseUp()
    {
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public override void OnInit(Controller controller)
    {
        SafeWriteLine("Initialized");
    }

    public override void OnConnect(Controller controller)
    {
        SafeWriteLine("Connected");

        var screen = controller.CalibratedScreens[0];
    }

    public override void OnDisconnect(Controller controller)
    {
        SafeWriteLine("Disconnected");
    }

    public override void OnExit(Controller controller)
    {
        SafeWriteLine("Exited");
    }

    public override void OnFrame(Controller controller)
    {
        // Get the most recent frame and report some basic information
        Frame frame = controller.Frame();

        var screens = controller.CalibratedScreens;

        if (!frame.Fingers.Empty)
        {
            var finger = frame.Fingers[0];

            var screen = controller.CalibratedScreens.ClosestScreenHit(finger);

            if (screen != null && screen.IsValid)
            {
                var intersect = screen.Intersect(finger, true);

                var velocity = finger.TipVelocity;

                velocities.Enqueue(velocity);
                if (velocities.Count > 10)
                {
                    velocities.Dequeue();
                }
                var avgVelocity = new Vector(velocities.Average(v => v.x), velocities.Average(v => v.y), velocities.Average(v => v.z));

                // use Z to hold weight
                intersect.z = 1;//Math.Min(1, velocity.Magnitude / 100);

                var pMag = (positions.Any() ? (ToScreen(intersect, screen) - ToScreen(positions.Last(), screen)).Magnitude : 100);

                if (avgVelocity.Magnitude > 5 || pMag > 50)
                {
                    positions.Enqueue(intersect);

                    if (positions.Count > 10)
                    {
                        positions.Dequeue();
                    }

                    var avgWeight = positions.Average(v => v.z);
                    var avgX = positions.Average(v => v.x * v.z) / avgWeight;
                    var avgY = positions.Average(v => v.y * v.z) / avgWeight;

                    var screenPos = ToScreen(new Vector(avgX, avgY, 0), screen);

                    SetCursorPos((int)screenPos.x, (int)screenPos.y);
                }
                else
                {
                    // not moving

                    if (frame.Fingers.Count > 1)
                    {
                        var thumb = frame.Fingers[1];

                        var thumbDist = thumb.TipPosition - finger.TipPosition;

                        if (thumbDistances.Any())
                        {
                            var avgThumbDist = Average(thumbDistances);

                            var thumbVelocity = Average(thumbVelocities);
                            //SafeWriteLine(thumbVelocity.Magnitude.ToString());

                            if (thumbDist.Magnitude < avgThumbDist.Magnitude - 0.5 && !thumbDown && thumbVelocity.Magnitude > 10)
                            {
                                thumbDown = true;
                                SafeWriteLine("Down");
                                sendMouseDown();

                                thumbDistances.Clear();
                            }
                            if (thumbDist.Magnitude > avgThumbDist.Magnitude && thumbDown && thumbVelocity.Magnitude > 10)
                            {
                                thumbDown = false;
                                SafeWriteLine("Up");
                                sendMouseUp();
                            }
                        }

                        thumbDistances.Enqueue(thumbDist);
                        if (thumbDistances.Count > 10)
                        {
                            thumbDistances.Dequeue();
                        }

                        thumbVelocities.Enqueue(thumb.TipVelocity);
                        if (thumbVelocities.Count > 5)
                        {
                            thumbVelocities.Dequeue();
                        }
                    }
                }
            }
        }
    }

    private Vector Average(IEnumerable<Vector> vectors)
    {
        return new Vector(vectors.Average(v => v.x), vectors.Average(v => v.y), vectors.Average(v => v.z));
    }

    private Vector ToScreen(Vector v, Screen s)
    {
        var screenX = (int)(v.x * s.WidthPixels);
        var screenY = (int)(s.HeightPixels - v.y * s.HeightPixels);

        return new Vector(screenX, screenY, 0);
    }
}

class Sample
{
    public static void Main()
    {
        // Create a sample listener and controller
        SampleListener listener = new SampleListener();
        Controller controller = new Controller();

        // Have the sample listener receive events from the controller
        controller.AddListener(listener);

        // Keep this process running until Enter is pressed
        Console.WriteLine("Press Enter to quit...");
        Console.ReadLine();

        // Remove the sample listener when done
        controller.RemoveListener(listener);
        controller.Dispose();
    }
}
