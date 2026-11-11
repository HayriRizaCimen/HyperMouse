using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace HyperMouse.Controls;

/// <summary>
/// Procedural high-resolution 3D mesh and texture builder for a realistic ergonomic gaming mouse.
/// Coordinate system:
///   X: -Left / +Right
///   Y: 0 (desk) to +Top
///   Z: -Front (nose/scroll wheel) to +Back (palm heel)
/// </summary>
public static class MouseMeshBuilder
{
    public const double Length = 3.80;
    public const double HalfLength = Length * 0.5;

    // Hinge Z coordinates for primary trigger rotation
    public const double TriggerRearTz = 0.375;
    public static readonly double TriggerHingeZ = TzToZ(TriggerRearTz);
    public static readonly double WheelCenterZ = TzToZ(0.195);
    public const double WheelCenterY = 0.715;
    public const double WheelRadius = 0.252;
    public const double WheelWidth = 0.165;

    public static double TzToZ(double tz) => -HalfLength + tz * Length;

    private static double Gauss(double x, double center, double sigma)
    {
        double d = (x - center) / sigma;
        return Math.Exp(-d * d);
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Rounded nose & tail silhouette multiplier in [0, 1].</summary>
    public static double PlanformCap(double tz)
    {
        tz = Math.Clamp(tz, 0.0, 1.0);
        double front = 1.0;
        if (tz < 0.20)
        {
            double s = 1.0 - tz / 0.20;
            front = Math.Pow(Math.Max(0.0, 1.0 - Math.Pow(s, 2.2)), 0.48);
        }
        double back = 1.0;
        if (tz > 0.72)
        {
            double s = (tz - 0.72) / 0.28;
            back = Math.Pow(Math.Max(0.0, 1.0 - Math.Pow(s, 2.15)), 0.48);
        }
        return front * back;
    }

    /// <summary>Left outer half-width (positive magnitude; actual X is negative).</summary>
    public static double LeftExtent(double tz)
    {
        double cap = PlanformCap(tz);
        double waist = -0.105 * Gauss(tz, 0.44, 0.14);
        double palm = 0.095 * Gauss(tz, 0.72, 0.16);
        return (0.93 + waist + palm) * cap;
    }

    /// <summary>Right outer half-width (positive magnitude; actual X is positive).</summary>
    public static double RightExtent(double tz)
    {
        double cap = PlanformCap(tz);
        double waist = -0.065 * Gauss(tz, 0.45, 0.14);
        double palm = 0.115 * Gauss(tz, 0.72, 0.16);
        return (0.93 + waist + palm) * cap;
    }

    /// <summary>Centerline top arch height Y along tz.</summary>
    public static double CenterHeight(double tz)
    {
        tz = Math.Clamp(tz, 0.0, 1.0);
        double arch = Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(tz, 1.08))), 0.85);
        return 0.21 + 1.05 * arch;
    }

    /// <summary>Top surface X coordinate for normalized width tx in [-1, 1].</summary>
    public static double TopX(double tz, double tx)
    {
        return tx < 0 ? tx * LeftExtent(tz) : tx * RightExtent(tz);
    }

    /// <summary>Top surface Y coordinate at (tz, tx in [-1, 1]), including ergonomic tilt & finger grooves.</summary>
    public static double TopY(double tz, double tx, bool includeFingerGrooves = true)
    {
        double hCenter = CenterHeight(tz);
        double dome = Math.Pow(Math.Max(0.0, 1.0 - Math.Pow(Math.Abs(tx), 2.35)), 0.56);

        // Shoulder height where top dome transitions into the side wall
        double yRim = 0.21 + 0.34 * Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(tz, 1.05))), 0.65);

        // Subtle ergonomic right-hand tilt (~3.5 degrees) in the middle/front
        double tiltMask = Math.Sin(Math.PI * Math.Clamp(tz, 0.0, 1.0));
        double ergoTilt = -0.055 * tx * tiltMask;

        double y = yRim + (hCenter - yRim) * dome + ergoTilt;

        // Concave fingertip comfort grooves on LMB and RMB
        if (includeFingerGrooves && tz < 0.38)
        {
            double grooveTzMask = SmoothStep(0.02, 0.09, tz) * (1.0 - SmoothStep(0.28, 0.37, tz));
            double leftGroove = Gauss(tx, -0.54, 0.19);
            double rightGroove = Gauss(tx, 0.54, 0.19);
            y -= 0.042 * grooveTzMask * (leftGroove + rightGroove);
        }

        return y;
    }

    /// <summary>
    /// Evaluates a point along the outer side flank at (tz, v in [0, 1]) where v=0 is bottom lip (y=0.075)
    /// and v=1 is the top shoulder rim (tx = ±1).
    /// </summary>
    public static Point3D FlankPoint(double tz, double v, bool isLeft, double radialOffset = 0.0)
    {
        double z = TzToZ(tz);
        double xRim = isLeft ? -LeftExtent(tz) : RightExtent(tz);
        double yTop = TopY(tz, isLeft ? -1.0 : 1.0, false);
        double yBot = 0.058;

        double y = yBot + (yTop - yBot) * v;

        double cap = PlanformCap(tz);
        // Subtle ergonomic inward taper near lower grip + thumb scoop on left flank
        double waistCurve = -0.080 * Math.Sin(Math.PI * Math.Pow(v, 0.85)) * SmoothStep(0.06, 0.25, tz) * (1.0 - SmoothStep(0.75, 0.96, tz));
        double thumbScoop = isLeft
            ? -0.052 * Gauss(tz, 0.45, 0.16) * Math.Sin(Math.PI * Math.Clamp(v, 0.0, 1.0))
            : 0.0;
        // Subtle flared thumb lip at bottom of left flank
        double thumbWing = isLeft
            ? 0.052 * Gauss(tz, 0.48, 0.17) * Math.Pow(1.0 - v, 2.4)
            : 0.0;

        // Slight inward bevel at the very bottom lip, tapered cleanly to 0 at nose/tail tips
        double bottomTuck = -0.038 * Math.Pow(1.0 - v, 3.0) * cap;

        double sign = isLeft ? -1.0 : 1.0;
        double xMag = Math.Max(0.0, Math.Abs(xRim) + waistCurve + thumbScoop + thumbWing + bottomTuck + radialOffset);
        double x = sign * xMag;

        return new Point3D(x, y, z);
    }

    /// <summary>Splits between palm hump and LMB/RMB triggers (curving around center DPI spine).</summary>
    private static double PalmSeamTz(double tx)
    {
        double centerBump = 0.055 * Gauss(tx, 0.0, 0.22);
        return 0.382 + centerBump;
    }

    // =========================================================
    // PROCEDURAL TEXTURES
    // =========================================================

    public static BitmapSource CreateShellTexture(Color topColor, Color edgeColor, double grainStrength = 4.5)
    {
        const int w = 256, h = 256;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[w * h * 4];
        uint seed = 0x51A7F321;

        for (int py = 0; py < h; py++)
        {
            double v = py / (double)(h - 1); // 0 = front, 1 = back (or top->edge)
            for (int px = 0; px < w; px++)
            {
                double u = px / (double)(w - 1);
                // Distance from center-axis U=0.5 for subtle AO shading toward edges
                double edgeDist = Math.Abs(u - 0.5) * 2.0;
                double ao = 1.0 - 0.24 * Math.Pow(edgeDist, 2.2) - 0.14 * Math.Pow(v, 2.5);

                seed = seed * 1664525u + 1013904223u;
                double noise = (((seed >> 24) & 0xFF) / 255.0 - 0.5) * grainStrength;

                double t = Math.Clamp(edgeDist * 0.65 + v * 0.25, 0.0, 1.0);
                double r = (topColor.R * (1 - t) + edgeColor.R * t) * ao + noise;
                double g = (topColor.G * (1 - t) + edgeColor.G * t) * ao + noise;
                double b = (topColor.B * (1 - t) + edgeColor.B * t) * ao + noise;

                int idx = (py * w + px) * 4;
                pixels[idx + 0] = (byte)Math.Clamp(b, 0, 255);
                pixels[idx + 1] = (byte)Math.Clamp(g, 0, 255);
                pixels[idx + 2] = (byte)Math.Clamp(r, 0, 255);
                pixels[idx + 3] = 255;
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        wb.Freeze();
        return wb;
    }

    public static BitmapSource CreateRubberGripTexture()
    {
        const int w = 128, h = 128;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[w * h * 4];
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                // Diagonal micro-knurled dot texture
                double gx = (px + py * 0.5) % 8.0 - 4.0;
                double gy = py % 8.0 - 4.0;
                double dot = Math.Exp(-(gx * gx + gy * gy) / 4.2);
                byte val = (byte)(18 + dot * 12);
                int idx = (py * w + px) * 4;
                pixels[idx + 0] = (byte)(val + 4);
                pixels[idx + 1] = (byte)(val + 1);
                pixels[idx + 2] = val;
                pixels[idx + 3] = 255;
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        wb.Freeze();
        return wb;
    }

    public static BitmapSource CreateFloorShadowTexture(Color rgbAccent)
    {
        const int w = 256, h = 256;
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[w * h * 4];
        for (int py = 0; py < h; py++)
        {
            double ny = (py / (double)(h - 1) - 0.5) * 2.0;
            for (int px = 0; px < w; px++)
            {
                double nx = (px / (double)(w - 1) - 0.5) * 2.0;
                double r = Math.Sqrt(nx * nx * 1.45 + ny * ny * 0.92);

                // Soft dark contact shadow under mouse + faint colored RGB underglow halo
                double contactShadow = Math.Exp(-Math.Pow(r / 0.52, 2.2));
                double glowRing = Math.Exp(-Math.Pow((r - 0.56) / 0.18, 2.0)) * 0.34;

                double alpha = Math.Clamp(contactShadow * 0.78 + glowRing * 0.45, 0.0, 1.0);
                double mixGlow = glowRing / (contactShadow + glowRing + 1e-4);

                byte cr = (byte)Math.Clamp(rgbAccent.R * mixGlow, 0, 255);
                byte cg = (byte)Math.Clamp(rgbAccent.G * mixGlow, 0, 255);
                byte cb = (byte)Math.Clamp(rgbAccent.B * mixGlow, 0, 255);

                int idx = (py * w + px) * 4;
                pixels[idx + 0] = cb;
                pixels[idx + 1] = cg;
                pixels[idx + 2] = cr;
                pixels[idx + 3] = (byte)(alpha * 255);
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        wb.Freeze();
        return wb;
    }

    // =========================================================
    // MAIN BODY PARTS
    // =========================================================

    /// <summary>Builds the upper palm hump, rear bevel, and center spine behind/around the wheel.</summary>
    public static MeshGeometry3D BuildPalmAndSpineShell()
    {
        var parts = new List<MeshGeometry3D>();

        // 1. Rear palm dome (from PalmSeamTz(tx) to tz = 1.0)
        const int nu = 44;
        const int nv = 34;
        var palmPts = new Point3D[nu, nv];
        var palmUvs = new Point[nu, nv];

        for (int i = 0; i < nu; i++)
        {
            double tx = -1.0 + 2.0 * i / (nu - 1);
            double tzStart = PalmSeamTz(tx);
            for (int j = 0; j < nv; j++)
            {
                double v = j / (double)(nv - 1);
                double tz = tzStart + (1.0 - tzStart) * v;
                palmPts[i, j] = new Point3D(TopX(tz, tx), TopY(tz, tx, false), TzToZ(tz));
                palmUvs[i, j] = new Point((tx + 1.0) * 0.5, tz);
            }
        }
        parts.Add(BuildGridMesh(palmPts, palmUvs, flipWinding: false));

        // Front lip bevel wall at the palm split seam (so there's a crisp shadow edge behind LMB/RMB)
        var seamPts = new Point3D[nu, 3];
        for (int i = 0; i < nu; i++)
        {
            double tx = -1.0 + 2.0 * i / (nu - 1);
            double tzStart = PalmSeamTz(tx);
            double x = TopX(tzStart, tx);
            double yTop = TopY(tzStart, tx, false);
            double z = TzToZ(tzStart);
            seamPts[i, 0] = new Point3D(x, yTop - 0.14, z + 0.04);
            seamPts[i, 1] = new Point3D(x, yTop - 0.03, z + 0.006);
            seamPts[i, 2] = new Point3D(x, yTop, z);
        }
        parts.Add(BuildGridMesh(seamPts, null, flipWinding: false));

        // 2. Front center spine bridge behind wheel (tz from 0.305 to PalmSeamTz, |x| <= 0.165)
        const int su = 14;
        const int sv = 10;
        var spinePts = new Point3D[su, sv];
        var spineUvs = new Point[su, sv];
        for (int i = 0; i < su; i++)
        {
            double s = -1.0 + 2.0 * i / (su - 1);
            double x = s * 0.165;
            for (int j = 0; j < sv; j++)
            {
                double v = j / (double)(sv - 1);
                double tz = 0.302 + (0.437 - 0.302) * v;
                double tx = x / Math.Max(0.25, LeftExtent(tz));
                double y = TopY(tz, tx, false) - 0.008;
                // Round down slightly at side edges of center spine
                y -= 0.025 * Math.Pow(Math.Abs(s), 2.5);
                spinePts[i, j] = new Point3D(x, y, TzToZ(tz));
                spineUvs[i, j] = new Point(0.5 + s * 0.08, tz);
            }
        }
        parts.Add(BuildGridMesh(spinePts, spineUvs, flipWinding: false));

        // 3. Front nose bridge ahead of scroll wheel (tz from 0.0 to 0.088, |x| <= 0.165)
        var noseBridgePts = new Point3D[su, 8];
        var noseBridgeUvs = new Point[su, 8];
        for (int i = 0; i < su; i++)
        {
            double s = -1.0 + 2.0 * i / (su - 1);
            for (int j = 0; j < 8; j++)
            {
                double v = j / 7.0;
                double tz = 0.005 + (0.086 - 0.005) * v;
                double halfW = Math.Min(0.165, LeftExtent(tz) * 0.85);
                double x = s * halfW;
                double tx = x / Math.Max(0.15, LeftExtent(tz));
                double y = TopY(tz, tx, false) - 0.015 - 0.02 * Math.Pow(Math.Abs(s), 2.5);
                noseBridgePts[i, j] = new Point3D(x, y, TzToZ(tz));
                noseBridgeUvs[i, j] = new Point(0.5 + s * 0.08, tz);
            }
        }
        parts.Add(BuildGridMesh(noseBridgePts, noseBridgeUvs, flipWinding: false));

        // 4. Outer Left & Right sculpted side flanks
        parts.Add(BuildSideFlank(isLeft: true));
        parts.Add(BuildSideFlank(isLeft: false));

        return MergeMeshes(parts);
    }

    private static MeshGeometry3D BuildSideFlank(bool isLeft)
    {
        const int nTz = 64;
        const int nV = 14;
        var pts = new Point3D[nTz, nV];
        var uvs = new Point[nTz, nV];

        for (int j = 0; j < nTz; j++)
        {
            double tz = j / (double)(nTz - 1);
            // In front trigger zone (tz < 0.382), rise smoothly to just beneath the floating button lip (v ~ 0.88)
            double maxV = tz < 0.395
                ? 0.88 + 0.12 * SmoothStep(0.365, 0.395, tz)
                : 1.0;

            for (int i = 0; i < nV; i++)
            {
                double v = (i / (double)(nV - 1)) * maxV;
                pts[j, i] = FlankPoint(tz, v, isLeft);
                uvs[j, i] = new Point(isLeft ? 0.05 + 0.1 * (1 - v) : 0.95 - 0.1 * (1 - v), tz);
            }
        }
        // Left flank: j (+Z), i (+Y). (+Y) x (+Z) = +X (inward on Left!). So flip = true!
        // Right flank: (+Y) x (+Z) = +X (outward on Right!). So flip = false!
        return BuildGridMesh(pts, uvs, flipWinding: isLeft);
    }

    /// <summary>Dark recessed inner cavity underneath floating LMB, RMB, and inside wheel well.</summary>
    public static MeshGeometry3D BuildInnerCavity()
    {
        var parts = new List<MeshGeometry3D>();

        // 1. Trigger bed floor under LMB/RMB (tz 0.03..0.38, tx -0.88..+0.88)
        const int nu = 28, nv = 16;
        var floor = new Point3D[nu, nv];
        for (int i = 0; i < nu; i++)
        {
            double tx = -0.88 + 1.76 * i / (nu - 1);
            for (int j = 0; j < nv; j++)
            {
                double tz = 0.03 + 0.35 * j / (nv - 1);
                double x = TopX(tz, tx);
                double y = TopY(tz, tx, false) - 0.115;
                if (Math.Abs(x) < 0.135 && tz is > 0.075 and < 0.31)
                {
                    y -= 0.19; // Deep wheel well pit
                }
                floor[i, j] = new Point3D(x, Math.Max(0.12, y), TzToZ(tz));
            }
        }
        parts.Add(BuildGridMesh(floor, null, flipWinding: false));

        // 2. Solid inner skirt backing behind front gaps so nothing ever sees through the side seam
        for (int side = 0; side < 2; side++)
        {
            bool isLeft = side == 0;
            const int nTz = 22, nV = 4;
            var backing = new Point3D[nTz, nV];
            for (int j = 0; j < nTz; j++)
            {
                double tz = 0.01 + 0.39 * j / (nTz - 1);
                for (int i = 0; i < nV; i++)
                {
                    double v = 0.35 + 0.57 * i / (nV - 1);
                    backing[j, i] = FlankPoint(tz, v, isLeft, radialOffset: -0.04);
                }
            }
            parts.Add(BuildGridMesh(backing, null, flipWinding: isLeft));
        }

        // 3. Front nose recessed USB-C port bezel (tz ~ 0.0, x in [-0.22, 0.22], y in [0.09, 0.26])
        var portBack = new Point3D[6, 4];
        for (int i = 0; i < 6; i++)
        {
            double x = -0.18 + 0.36 * i / 5.0;
            for (int j = 0; j < 4; j++)
            {
                double y = 0.10 + 0.15 * j / 3.0;
                portBack[i, j] = new Point3D(x, y, TzToZ(0.012));
            }
        }
        parts.Add(BuildGridMesh(portBack, null, flipWinding: false));

        return MergeMeshes(parts);
    }

    /// <summary>Builds a solid, beveled, finger-grooved Left or Right trigger button plate.</summary>
    public static MeshGeometry3D BuildTriggerButton(bool isLeft)
    {
        const int nu = 22;
        const int nv = 26;
        const double tzMin = 0.014;
        const double tzMax = TriggerRearTz;

        double txInner = isLeft ? -0.205 : 0.205;
        double txOuter = isLeft ? -0.985 : 0.985;

        var top = new Point3D[nu, nv];
        var bot = new Point3D[nu, nv];
        var uvs = new Point[nu, nv];

        for (int i = 0; i < nu; i++)
        {
            double u = i / (double)(nu - 1); // 0 = left-most, 1 = right-most in grid
            double tx = isLeft
                ? txOuter + (txInner - txOuter) * u
                : txInner + (txOuter - txInner) * u;

            for (int j = 0; j < nv; j++)
            {
                double v = j / (double)(nv - 1); // 0 = front tip, 1 = rear hinge
                double tz = tzMin + (tzMax - tzMin) * v;

                // Subtle edge rounding & chamfer along perimeter of button cap
                double edgeDistU = Math.Min(u, 1.0 - u);
                double edgeDistV = Math.Min(v, 1.0 - v);
                double chamfer = 0.016 * (1.0 - SmoothStep(0.0, 0.09, Math.Min(edgeDistU, edgeDistV)));

                // Taper width slightly at very front tip for crisp nose alignment
                double nosePull = 1.0 - 0.14 * Math.Pow(1.0 - v, 3.0);
                double effTx = tx * nosePull;

                double x = TopX(tz, effTx);
                double yTop = TopY(tz, effTx, includeFingerGrooves: true) - chamfer;
                double z = TzToZ(tz);

                double thickness = 0.068 - 0.032 * (1.0 - SmoothStep(0.0, 0.12, Math.Min(edgeDistU, edgeDistV)));

                top[i, j] = new Point3D(x, yTop, z);
                bot[i, j] = new Point3D(x, yTop - thickness, z);
                uvs[i, j] = new Point((effTx + 1.0) * 0.5, tz);
            }
        }

        return BuildSolidPatch(top, bot, uvs);
    }

    /// <summary>Builds the knurled 3D rubber scroll wheel tire with 24 sculpted treads.</summary>
    public static MeshGeometry3D BuildScrollWheelTire()
    {
        const int teeth = 24;
        const int segsPerTooth = 3;
        int nTheta = teeth * segsPerTooth + 1;
        const int nW = 9;

        double halfW = WheelWidth * 0.5;
        var outer = new Point3D[nW, nTheta];
        var inner = new Point3D[nW, nTheta];

        for (int j = 0; j < nTheta; j++)
        {
            double angle = (j % (nTheta - 1)) / (double)(nTheta - 1) * Math.PI * 2.0;
            double toothPhase = (j % segsPerTooth) / (double)segsPerTooth;
            // Raised ridge on toothPhase == 0..1, groove on 2
            double ridge = toothPhase < 0.5 ? 0.016 : -0.008;

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            for (int i = 0; i < nW; i++)
            {
                double u = i / (double)(nW - 1); // 0..1 across width (-X to +X)
                double x = -halfW + WheelWidth * u;
                double edgeBevel = Math.Abs(u - 0.5) * 2.0;
                double rOut = WheelRadius + ridge * (1.0 - 0.7 * Math.Pow(edgeBevel, 2.0)) - 0.014 * Math.Pow(edgeBevel, 3.0);
                double rIn = WheelRadius * 0.68;

                outer[i, j] = new Point3D(x, WheelCenterY + rOut * sin, WheelCenterZ + rOut * cos);
                inner[i, j] = new Point3D(x, WheelCenterY + rIn * sin, WheelCenterZ + rIn * cos);
            }
        }

        // Outer cylindrical tire surface
        // Note: i goes +X, j increases angle -> (cos, sin) -> d/dtheta = (0, cos, -sin).
        // At angle=0 (front, +Z): d/du=(1,0,0), d/dtheta=(0,+Y,0). (+X) x (+Y) = +Z (outward!).
        // Check: BuildGridMesh computes (p01 - p00) x (p10 - p00) = d/dtheta x d/du = (+Y) x (+X) = -Z (inward!).
        // So outer tire needs flipWinding = true!
        var parts = new List<MeshGeometry3D>
        {
            BuildGridMesh(outer, null, flipWinding: true)
        };

        // Side walls of the rubber tire
        for (int side = 0; side < 2; side++)
        {
            int wi = side == 0 ? 0 : nW - 1;
            var ring = new Point3D[2, nTheta];
            for (int j = 0; j < nTheta; j++)
            {
                ring[0, j] = inner[wi, j];
                ring[1, j] = outer[wi, j];
            }
            // Side 0 (-X) needs flip=true (-X outward); side 1 (+X) needs flip=false (+X outward)
            parts.Add(BuildGridMesh(ring, null, flipWinding: side == 0));
        }

        return MergeMeshes(parts);
    }

    /// <summary>Builds the brushed metallic inner hub & glowing side rings of the scroll wheel.</summary>
    public static MeshGeometry3D BuildScrollWheelHub()
    {
        const int nTheta = 36;
        double halfW = WheelWidth * 0.5 + 0.011; // Slightly proud metallic lip
        double rHub = WheelRadius * 0.71;
        var parts = new List<MeshGeometry3D>();

        for (int side = 0; side < 2; side++)
        {
            double x = side == 0 ? -halfW : halfW;
            var disc = new Point3D[2, nTheta + 1];
            for (int j = 0; j <= nTheta; j++)
            {
                double a = (j % nTheta) / (double)nTheta * Math.PI * 2.0;
                disc[0, j] = new Point3D(x, WheelCenterY, WheelCenterZ);
                disc[1, j] = new Point3D(x, WheelCenterY + rHub * Math.Sin(a), WheelCenterZ + rHub * Math.Cos(a));
            }
            parts.Add(BuildGridMesh(disc, null, flipWinding: side == 0));
        }

        // Hub rim cylinder
        var rim = new Point3D[2, nTheta + 1];
        for (int j = 0; j <= nTheta; j++)
        {
            double a = (j % nTheta) / (double)nTheta * Math.PI * 2.0;
            double ry = WheelCenterY + rHub * Math.Sin(a);
            double rz = WheelCenterZ + rHub * Math.Cos(a);
            rim[0, j] = new Point3D(-halfW, ry, rz);
            rim[1, j] = new Point3D(halfW, ry, rz);
        }
        parts.Add(BuildGridMesh(rim, null, flipWinding: true));

        return MergeMeshes(parts);
    }

    /// <summary>Builds the beveled 3D DPI pill button sitting on the center spine behind the wheel.</summary>
    public static MeshGeometry3D BuildDpiButton()
    {
        const int nu = 12;
        const int nv = 14;
        const double tzMin = 0.326;
        const double tzMax = 0.408;

        var top = new Point3D[nu, nv];
        var bot = new Point3D[nu, nv];
        var uvs = new Point[nu, nv];

        for (int i = 0; i < nu; i++)
        {
            double u = i / (double)(nu - 1); // 0..1
            for (int j = 0; j < nv; j++)
            {
                double v = j / (double)(nv - 1); // 0..1
                double tz = tzMin + (tzMax - tzMin) * v;

                // Diamond/oval tapered pill width
                double profile = Math.Sqrt(Math.Max(0.18, 1.0 - Math.Pow((v - 0.5) * 2.0, 2.4)));
                double x = (u - 0.5) * 2.0 * 0.088 * profile;
                double tx = x / Math.Max(0.2, LeftExtent(tz));

                double edgeDist = Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v));
                double domeLift = 0.036 * SmoothStep(0.0, 0.28, edgeDist);
                double yBase = TopY(tz, tx, false) - 0.005;

                top[i, j] = new Point3D(x, yBase + domeLift, TzToZ(tz));
                bot[i, j] = new Point3D(x, yBase - 0.035, TzToZ(tz));
                uvs[i, j] = new Point(u, v);
            }
        }
        return BuildSolidPatch(top, bot, uvs);
    }

    /// <summary>Builds curved 3D side thumb button X2 (forward=true) or X1 (forward=false) hugging the left flank.</summary>
    public static MeshGeometry3D BuildSideButton(bool isForward)
    {
        double tzMin = isForward ? 0.325 : 0.472;
        double tzMax = isForward ? 0.456 : 0.602;
        const double vMin = 0.56;
        const double vMax = 0.80;

        const int nTz = 14;
        const int nV = 10;

        // Grid i maps to v (+Y), j maps to tz (+Z) so outer points toward -X
        var outer = new Point3D[nV, nTz];
        var inner = new Point3D[nV, nTz];
        var uvs = new Point[nV, nTz];

        for (int i = 0; i < nV; i++)
        {
            double w = i / (double)(nV - 1);
            double v = vMin + (vMax - vMin) * w;
            for (int j = 0; j < nTz; j++)
            {
                double u = j / (double)(nTz - 1);
                double tz = tzMin + (tzMax - tzMin) * u;

                double edge = Math.Min(Math.Min(u, 1.0 - u), Math.Min(w, 1.0 - w));
                double extrude = 0.044 + 0.068 * SmoothStep(0.0, 0.24, edge);

                // Angled ergonomic ridge (slightly prouder along bottom thumb catch)
                extrude += 0.014 * (1.0 - w);

                outer[i, j] = FlankPoint(tz, v, isLeft: true, radialOffset: extrude);
                inner[i, j] = FlankPoint(tz, v, isLeft: true, radialOffset: -0.025);
                uvs[i, j] = new Point(u, w);
            }
        }

        return BuildSolidPatch(outer, inner, uvs);
    }

    /// <summary>Builds textured rubberized grip pad for left thumb or right ring/pinky finger.</summary>
    public static MeshGeometry3D BuildSideGrip(bool isLeft)
    {
        double tzMin = 0.28;
        double tzMax = isLeft ? 0.67 : 0.69;
        double vMin = 0.09;
        double vMax = isLeft ? 0.47 : 0.52;

        const int nTz = 36;
        const int nV = 14;
        var outer = new Point3D[nV, nTz];
        var inner = new Point3D[nV, nTz];
        var uvs = new Point[nV, nTz];

        for (int i = 0; i < nV; i++)
        {
            double w = i / (double)(nV - 1);
            double v = vMin + (vMax - vMin) * w;
            for (int j = 0; j < nTz; j++)
            {
                double u = j / (double)(nTz - 1);
                double tz = tzMin + (tzMax - tzMin) * u;
                double edge = Math.Min(Math.Min(u, 1.0 - u), Math.Min(w, 1.0 - w));
                // Tuck perimeter cleanly into the flank to eliminate Z-fighting
                double lift = -0.008 + 0.024 * SmoothStep(0.0, 0.16, edge);

                outer[i, j] = FlankPoint(tz, v, isLeft, radialOffset: lift);
                inner[i, j] = FlankPoint(tz, v, isLeft, radialOffset: -0.022);
                uvs[i, j] = new Point(u * 2.5, w * 1.5);
            }
        }

        return isLeft ? BuildSolidPatch(outer, inner, uvs) : BuildSolidPatch(inner, outer, uvs);
    }

    /// <summary>Builds the perimeter RGB underglow diffuser band wrapping around the bottom rim.</summary>
    public static MeshGeometry3D BuildRgbUnderglow()
    {
        const int nRing = 96;
        const int nH = 4;
        var pts = new Point3D[nRing, nH];

        for (int i = 0; i < nRing; i++)
        {
            double a = i / (double)(nRing - 1) * Math.PI * 2.0;
            // Map angle around mouse perimeter: cos -> z, sin -> x
            double tz = 0.5 - 0.495 * Math.Cos(a);
            double xSign = Math.Sin(a);
            bool isLeft = xSign < 0;

            Point3D basePt = FlankPoint(tz, 0.0, isLeft);
            double xBlend = Math.Abs(xSign);
            double x = basePt.X * Math.Pow(xBlend, 0.35) * Math.Sign(xSign);
            if (Math.Abs(xSign) < 1e-4) x = 0;

            double z = basePt.Z;
            for (int j = 0; j < nH; j++)
            {
                double h = j / (double)(nH - 1);
                double y = 0.024 + 0.036 * h;
                double expand = -0.008 + 0.014 * Math.Sin(Math.PI * h);
                pts[i, j] = new Point3D(x * (1.0 + expand), y, z);
            }
        }
        return BuildGridMesh(pts, null, flipWinding: false);
    }

    /// <summary>Builds the flat dark base plate, PTFE skates, and optical sensor window underneath.</summary>
    public static MeshGeometry3D BuildBasePlate()
    {
        const int nTz = 32;
        const int nTx = 20;
        var pts = new Point3D[nTx, nTz];

        for (int i = 0; i < nTx; i++)
        {
            double tx = -1.0 + 2.0 * i / (nTx - 1);
            for (int j = 0; j < nTz; j++)
            {
                double tz = j / (double)(nTz - 1);
                double x = TopX(tz, tx) * 0.94;
                pts[i, j] = new Point3D(x, 0.032, TzToZ(tz));
            }
        }
        return BuildGridMesh(pts, null, flipWinding: true); // Facing down (-Y)
    }

    public static MeshGeometry3D BuildBottomSkates()
    {
        var parts = new List<MeshGeometry3D>();
        // Front PTFE skate (tz 0.08..0.18) and Rear PTFE skate (tz 0.78..0.90)
        parts.Add(BuildSkatePatch(0.08, 0.17, 0.74));
        parts.Add(BuildSkatePatch(0.79, 0.90, 0.76));
        return MergeMeshes(parts);
    }

    private static MeshGeometry3D BuildSkatePatch(double tzMin, double tzMax, double txSpan)
    {
        const int nx = 14, nz = 8;
        var pts = new Point3D[nx, nz];
        for (int i = 0; i < nx; i++)
        {
            double tx = -txSpan + 2.0 * txSpan * i / (nx - 1);
            for (int j = 0; j < nz; j++)
            {
                double tz = tzMin + (tzMax - tzMin) * j / (nz - 1);
                pts[i, j] = new Point3D(TopX(tz, tx), 0.008, TzToZ(tz));
            }
        }
        return BuildGridMesh(pts, null, flipWinding: true);
    }

    /// <summary>Builds a subtle curved geometric HyperMouse emblem sitting flush on the palm arch.</summary>
    public static MeshGeometry3D BuildPalmLogo()
    {
        var parts = new List<MeshGeometry3D>();
        // Twin sleek chevron wings on the palm hump (tz ~ 0.71..0.77)
        for (int wing = 0; wing < 2; wing++)
        {
            double sign = wing == 0 ? -1.0 : 1.0;
            const int nu = 8, nv = 4;
            var top = new Point3D[nu, nv];
            var bot = new Point3D[nu, nv];
            for (int i = 0; i < nu; i++)
            {
                double u = i / (double)(nu - 1);
                double tx = sign * (0.012 + 0.115 * u);
                for (int j = 0; j < nv; j++)
                {
                    double v = j / (double)(nv - 1);
                    double tz = 0.715 + 0.042 * u + 0.014 * v;
                    double x = TopX(tz, tx);
                    double y = TopY(tz, tx, false);
                    top[i, j] = new Point3D(x, y + 0.008, TzToZ(tz));
                    bot[i, j] = new Point3D(x, y - 0.006, TzToZ(tz));
                }
            }
            parts.Add(wing == 0 ? BuildSolidPatch(bot, top, null) : BuildSolidPatch(top, bot, null));
        }
        return MergeMeshes(parts);
    }

    /// <summary>Builds the optical sensor lens ring on the underside of the mouse.</summary>
    public static MeshGeometry3D BuildOpticalSensor()
    {
        const int nTheta = 28;
        const double zc = 0.15;
        const double rIn = 0.10;
        const double rOut = 0.22;
        var ring = new Point3D[2, nTheta + 1];
        for (int j = 0; j <= nTheta; j++)
        {
            double a = (j % nTheta) / (double)nTheta * Math.PI * 2.0;
            ring[0, j] = new Point3D(rIn * Math.Cos(a), 0.026, zc + rIn * Math.Sin(a));
            ring[1, j] = new Point3D(rOut * Math.Cos(a), 0.026, zc + rOut * Math.Sin(a));
        }
        // Facing down (-Y): first is radial outward, second is clockwise around +Y (cos, sin) -> tangent (-sin, cos).
        // (+Z at a=0 is x=R, z=0: r=(1,0,0), t=(0,0,1). t x r = (+Z) x (+X) = +Y. Flip for -Y!
        return BuildGridMesh(ring, null, flipWinding: true);
    }

    /// <summary>Horizontal studio ground quad under the mouse for soft shadow & RGB bounce.</summary>
    public static MeshGeometry3D BuildStudioFloor()
    {
        const double sx = 2.6;
        const double sz = 3.6;
        const double y = -0.005;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(-sx, y, -sz));
        mesh.Positions.Add(new Point3D(-sx, y, sz));
        mesh.Positions.Add(new Point3D(sx, y, sz));
        mesh.Positions.Add(new Point3D(sx, y, -sz));

        mesh.TextureCoordinates.Add(new Point(0, 0));
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));

        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);
        return mesh;
    }

    // =========================================================
    // GEOMETRY BUILDING UTILITIES
    // =========================================================

    private static MeshGeometry3D BuildSolidPatch(Point3D[,] top, Point3D[,] bot, Point[,]? uvs)
    {
        int nu = top.GetLength(0);
        int nv = top.GetLength(1);
        var parts = new List<MeshGeometry3D>
        {
            BuildGridMesh(top, uvs, flipWinding: false),
            BuildGridMesh(bot, uvs, flipWinding: true)
        };

        // 4 perimeter walls connecting bot to top
        var sideU0 = new Point3D[2, nv];
        var sideU1 = new Point3D[2, nv];
        for (int j = 0; j < nv; j++)
        {
            sideU0[0, j] = bot[0, j];
            sideU0[1, j] = top[0, j];
            sideU1[0, j] = bot[nu - 1, j];
            sideU1[1, j] = top[nu - 1, j];
        }
        parts.Add(BuildGridMesh(sideU0, null, flipWinding: false));
        parts.Add(BuildGridMesh(sideU1, null, flipWinding: true));

        var sideV0 = new Point3D[nu, 2];
        var sideV1 = new Point3D[nu, 2];
        for (int i = 0; i < nu; i++)
        {
            sideV0[i, 0] = bot[i, 0];
            sideV0[i, 1] = top[i, 0];
            sideV1[i, 0] = bot[i, nv - 1];
            sideV1[i, 1] = top[i, nv - 1];
        }
        parts.Add(BuildGridMesh(sideV0, null, flipWinding: false));
        parts.Add(BuildGridMesh(sideV1, null, flipWinding: true));

        return MergeMeshes(parts);
    }

    private static MeshGeometry3D BuildGridMesh(Point3D[,] pts, Point[,]? uvs, bool flipWinding)
    {
        int nu = pts.GetLength(0);
        int nv = pts.GetLength(1);

        var mesh = new MeshGeometry3D();
        var normals = new Vector3D[nu * nv];

        for (int i = 0; i < nu; i++)
        {
            for (int j = 0; j < nv; j++)
            {
                mesh.Positions.Add(pts[i, j]);
                mesh.TextureCoordinates.Add(uvs != null
                    ? uvs[i, j]
                    : new Point(i / (double)Math.Max(1, nu - 1), j / (double)Math.Max(1, nv - 1)));
            }
        }

        for (int i = 0; i < nu - 1; i++)
        {
            for (int j = 0; j < nv - 1; j++)
            {
                int i00 = i * nv + j;
                int i10 = (i + 1) * nv + j;
                int i01 = i * nv + (j + 1);
                int i11 = (i + 1) * nv + (j + 1);

                if (!flipWinding)
                {
                    AddTriangle(mesh, normals, i00, i01, i11);
                    AddTriangle(mesh, normals, i00, i11, i10);
                }
                else
                {
                    AddTriangle(mesh, normals, i00, i11, i01);
                    AddTriangle(mesh, normals, i00, i10, i11);
                }
            }
        }

        for (int k = 0; k < normals.Length; k++)
        {
            if (normals[k].LengthSquared > 1e-12) normals[k].Normalize();
            else normals[k] = new Vector3D(0, 1, 0);
            mesh.Normals.Add(normals[k]);
        }

        return mesh;
    }

    private static void AddTriangle(MeshGeometry3D mesh, Vector3D[] normals, int a, int b, int c)
    {
        Point3D pa = mesh.Positions[a];
        Point3D pb = mesh.Positions[b];
        Point3D pc = mesh.Positions[c];

        Vector3D faceNormal = Vector3D.CrossProduct(pb - pa, pc - pa);
        if (faceNormal.LengthSquared < 1e-16) return;

        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(b);
        mesh.TriangleIndices.Add(c);

        normals[a] += faceNormal;
        normals[b] += faceNormal;
        normals[c] += faceNormal;
    }

    private static MeshGeometry3D MergeMeshes(IEnumerable<MeshGeometry3D> meshes)
    {
        var result = new MeshGeometry3D();
        int offset = 0;
        foreach (var m in meshes)
        {
            foreach (var p in m.Positions) result.Positions.Add(p);
            foreach (var n in m.Normals) result.Normals.Add(n);
            foreach (var uv in m.TextureCoordinates) result.TextureCoordinates.Add(uv);
            foreach (int idx in m.TriangleIndices) result.TriangleIndices.Add(offset + idx);
            offset += m.Positions.Count;
        }
        return result;
    }
}
