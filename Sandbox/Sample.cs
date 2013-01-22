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

    private LimitQueue<Vector> positionAverage = new LimitQueue<Vector>(30);
    private LimitQueue<Vector> velocityAverage = new LimitQueue<Vector>(10);
    private LimitQueue<float> extraAverage = new LimitQueue<float>(10);

    private int haltTime = 0;
    private bool isMouseDown;
    private Vector lastMousePos = Vector.Zero;

    private void SafeWriteLine(String line)
    {
        lock (thisLock)
        {
            Console.WriteLine(line);
        }
    }

    private void sendMouseDown()
    {
        if (!isMouseDown)
        {
            SafeWriteLine("Down");
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            isMouseDown = true;
        }
    }

    private void sendMouseUp()
    {
        if (isMouseDown)
        {
            SafeWriteLine("Up");
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            isMouseDown = false;
        }
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

        if (!frame.Hands.Empty)
        {
            var hand = frame.Hands[0];

            var screen = controller.CalibratedScreens.FirstOrDefault();

            if (screen != null && screen.IsValid)
            {
                var pos = FindCursorPosition(hand, screen);

                SetCursorPos((int)pos.x, (int)pos.y);
            }
        }
    }

    private Vector FindCursorPosition(Hand hand, Screen screen)
    {
        if (hand.Fingers.Count > 1)
        {
            VelocityClickMethod(hand.Fingers);
        }

        var position = PalmDirectionMethod(hand, screen);
        var diff = position - lastMousePos;
        lastMousePos = position;

        velocityAverage.Enqueue(diff);

        if (Average(velocityAverage).Magnitude > 0.5 && haltTime == 0 || !positionAverage.Any())
        {
            positionAverage.Enqueue(position);
        }

        if (haltTime > 0)
        {
            haltTime--;
        }

        return Average(positionAverage);
    }

    private Vector PalmDirectionMethod(Hand hand, Screen screen)
    {
        var handScreenIntersection = VectorPlaneIntersection(hand.PalmPosition, hand.Direction, screen.BottomLeftCorner, screen.Normal());

        var screenPoint = handScreenIntersection - screen.BottomLeftCorner;
        var screenPointHorizontal = screenPoint.Dot(screen.HorizontalAxis.Normalized) / screen.HorizontalAxis.Magnitude;
        var screenPointVertical = screenPoint.Dot(screen.VerticalAxis.Normalized) / screen.VerticalAxis.Magnitude;
        var screenRatios = new Vector(screenPointHorizontal, screenPointVertical, 0);

        // do some calibration adjustments
        screenRatios.x = (screenRatios.x - 0.5f) * 1.7f + 0.3f; // increase X sensitivity by 70% and shift left 20%
        screenRatios.y = (screenRatios.y - 0.5f) * 1.5f + 0.5f; // increase X sensitivity by 50%

        var screenCoords = ToScreen(screenRatios, screen);

        return screenCoords;
    }

    private Vector ScreenFrustumMethod(Vector handPosition, Screen screen)
    {
        var distance = screen.DistanceToPoint(handPosition) + 100;

        var frustumScale = 1 - (distance / 600);

        var scaledPosition = handPosition / frustumScale;

        var screenWidth = screen.HorizontalAxis.Magnitude;
        var screenHeight = screen.VerticalAxis.Magnitude;

        var normalizedPosition = new Vector(scaledPosition.x / screenWidth + 0.5f, scaledPosition.y / screenHeight - 1f, 0);

        var screenPosition = ToScreen(normalizedPosition, screen);

        return screenPosition;
    }

    private void VelocityClickMethod(IEnumerable<Finger> fingers)
    {
        var fingerVelocity = fingers.Average(f => f.TipVelocity.y);

        var fingerVelocityVariance = fingers.Sum(f => Math.Pow(f.TipVelocity.y - fingerVelocity, 2)) / fingers.Count();
        var fingerVelocityDeviation = (float)Math.Sqrt(fingerVelocityVariance);

        if (fingerVelocityDeviation > 100)
        {
            float greatestDeviation = 0;
            Finger greatestDeviator = null;
            foreach (var finger in fingers)
            {
                var deviation = Math.Abs(finger.TipVelocity.y - fingerVelocity) / fingerVelocityDeviation;

                if (deviation > greatestDeviation)
                {
                    greatestDeviation = deviation;
                    greatestDeviator = finger;
                }
            }

            if (greatestDeviation > 0.5 && greatestDeviator != null)
            {
                // stop mouse movement
                haltTime = 30;

                if (greatestDeviator.TipVelocity.y < 0)
                {
                    sendMouseDown();
                }
                else
                {
                    sendMouseUp();
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
        var screenX = (int)Math.Min(s.WidthPixels, Math.Max(0, (v.x * s.WidthPixels)));
        var screenY = (int)Math.Min(s.HeightPixels, Math.Max(0, (s.HeightPixels - v.y * s.HeightPixels)));

        return new Vector(screenX, screenY, 0);
    }

    private Vector VectorPlaneIntersection(Vector vectorPoint, Vector vectorDirection, Vector planePoint, Vector planeNormal)
    {
        float distance = ((planePoint - vectorPoint).Dot(planeNormal) / (vectorDirection.Dot(planeNormal)));

        return vectorPoint + vectorDirection * distance;
    }
}

class LimitQueue<T> : Queue<T>
{
    public int Max { get; set; }

    public LimitQueue(int max)
    {
        Max = max;
    }

    public new void Enqueue(T item)
    {
        base.Enqueue(item);

        while (Count > Max)
        {
            Dequeue();
        }
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
