using System;
using System.Collections.Generic;

namespace DCSMissionReader
{
    /// <summary>
    /// Helper class for map coordinate transformations using Transverse Mercator projection.
    /// 
    /// DCS World uses a Transverse Mercator projection for each map.
    /// In DCS mission files with axis=neu convention:
    /// - DCS X coordinate = Northing (positive = North)
    /// - DCS Y coordinate = Easting (positive = East)
    /// 
    /// The projections use WGS84 ellipsoid.
    /// </summary>
    public static class MapHelper
    {
        // WGS84 ellipsoid constants
        private const double A = 6378137.0;                  // Semi-major axis in meters
        private const double F = 1.0 / 298.257223563;        // Flattening
        private const double E2 = 2 * F - F * F;             // First eccentricity squared: 0.00669437999014
        private static readonly double E1 = (1 - Math.Sqrt(1 - E2)) / (1 + Math.Sqrt(1 - E2));

        /// <summary>
        /// Theater projection parameters
        /// Data from: https://github.com/JonathanTurnock/dcs-projections/blob/main/projections.json
        /// 
        /// These define PROJ4-style Transverse Mercator projections with axis=neu
        /// x_0 = false easting, y_0 = false northing
        /// </summary>
        private class TheaterProjection
        {
            public double CentralMeridian { get; set; }  // lon_0 in degrees
            public double ScaleFactor { get; set; }      // k_0  
            public double FalseEasting { get; set; }     // x_0 - added to Easting
            public double FalseNorthing { get; set; }    // y_0 - added to Northing
        }

        private static readonly Dictionary<string, TheaterProjection> Projections = new Dictionary<string, TheaterProjection>(StringComparer.OrdinalIgnoreCase)
        {
            // Official projections from dcs-projections repository
            { "Caucasus", new TheaterProjection { CentralMeridian = 33, ScaleFactor = 0.9996, FalseEasting = -99516.9999999732, FalseNorthing = -4998114.999999984 } },
            { "Syria", new TheaterProjection { CentralMeridian = 39, ScaleFactor = 0.9996, FalseEasting = 282801.00000003993, FalseNorthing = -3879865.9999999935 } },
            { "PersianGulf", new TheaterProjection { CentralMeridian = 57, ScaleFactor = 0.9996, FalseEasting = 75755.99999999645, FalseNorthing = -2894933.0000000377 } },
            { "Nevada", new TheaterProjection { CentralMeridian = -117, ScaleFactor = 0.9996, FalseEasting = -193996.80999964548, FalseNorthing = -4410028.063999966 } },
            { "MarianaIslands", new TheaterProjection { CentralMeridian = 147, ScaleFactor = 0.9996, FalseEasting = 238417.99999989968, FalseNorthing = -1491840.000000048 } },
            { "MarianaIslandsWWII", new TheaterProjection { CentralMeridian = 147, ScaleFactor = 0.9996, FalseEasting = 238417.99999989968, FalseNorthing = -1491840.000000048 } },
            { "SouthAtlantic", new TheaterProjection { CentralMeridian = -57, ScaleFactor = 0.9996, FalseEasting = 147639.99999997593, FalseNorthing = 5815417.000000032 } },
            { "Falklands", new TheaterProjection { CentralMeridian = -57, ScaleFactor = 0.9996, FalseEasting = 147639.99999997593, FalseNorthing = 5815417.000000032 } },
            { "Normandy", new TheaterProjection { CentralMeridian = -3, ScaleFactor = 0.9996, FalseEasting = -195526.00000000204, FalseNorthing = -5484812.999999951 } },
            { "TheChannel", new TheaterProjection { CentralMeridian = 3, ScaleFactor = 0.9996, FalseEasting = 99376.00000000288, FalseNorthing = -5636889.00000001 } },
            
            // Estimated projections for newer maps (need verification)
            { "Sinai", new TheaterProjection { CentralMeridian = 33, ScaleFactor = 0.9996, FalseEasting = 250000, FalseNorthing = -3300000 } },
            { "Kola", new TheaterProjection { CentralMeridian = 33, ScaleFactor = 0.9996, FalseEasting = 500000, FalseNorthing = -7550000 } },
            { "Afghanistan", new TheaterProjection { CentralMeridian = 69, ScaleFactor = 0.9996, FalseEasting = 200000, FalseNorthing = -3850000 } },
        };

        /// <summary>
        /// Convert DCS coordinates to Latitude/Longitude.
        /// 
        /// In DCS with axis=neu projection convention:
        /// - dcsX = Northing coordinate (meters)
        /// - dcsY = Easting coordinate (meters)
        /// 
        /// The inverse transform: subtract false values then apply inverse TM projection.
        /// </summary>
        public static (double Lat, double Lon) DcsToLatLon(string theater, double dcsX, double dcsY)
        {
            if (!Projections.TryGetValue(theater ?? "Caucasus", out var proj))
            {
                proj = Projections["Caucasus"];
            }

            // Remove false northing/easting to get true projected coordinates
            // In axis=neu: DCS X is Northing, DCS Y is Easting
            double northing = dcsX - proj.FalseNorthing;
            double easting = dcsY - proj.FalseEasting;

            // Apply inverse Transverse Mercator
            return InverseTransverseMercator(northing, easting, proj.CentralMeridian, proj.ScaleFactor);
        }

        /// <summary>
        /// Inverse Transverse Mercator projection
        /// Converts projected coordinates to geographic coordinates
        /// Based on Snyder's "Map Projections: A Working Manual" formulas
        /// </summary>
        private static (double Lat, double Lon) InverseTransverseMercator(double northing, double easting, double lon0Deg, double k0)
        {
            double lon0 = lon0Deg * Math.PI / 180.0;

            // Calculate meridional arc M from northing
            double M = northing / k0;

            // Calculate footpoint latitude (the latitude at which the meridian arc equals M)
            double mu = M / (A * (1 - E2 / 4 - 3 * Math.Pow(E2, 2) / 64 - 5 * Math.Pow(E2, 3) / 256));

            // Footpoint latitude using series expansion
            double phi1 = mu
                + (3 * E1 / 2 - 27 * Math.Pow(E1, 3) / 32) * Math.Sin(2 * mu)
                + (21 * Math.Pow(E1, 2) / 16 - 55 * Math.Pow(E1, 4) / 32) * Math.Sin(4 * mu)
                + (151 * Math.Pow(E1, 3) / 96) * Math.Sin(6 * mu)
                + (1097 * Math.Pow(E1, 4) / 512) * Math.Sin(8 * mu);

            // Calculate values at footpoint latitude
            double sinPhi1 = Math.Sin(phi1);
            double cosPhi1 = Math.Cos(phi1);
            double tanPhi1 = Math.Tan(phi1);

            // Radius of curvature in prime vertical
            double N1 = A / Math.Sqrt(1 - E2 * sinPhi1 * sinPhi1);

            // Radius of curvature in meridian
            double R1 = A * (1 - E2) / Math.Pow(1 - E2 * sinPhi1 * sinPhi1, 1.5);

            // Second eccentricity squared times cos^2(phi)
            double ep2 = E2 / (1 - E2);
            double C1 = ep2 * cosPhi1 * cosPhi1;

            double T1 = tanPhi1 * tanPhi1;

            // D = easting / (N1 * k0)
            double D = easting / (N1 * k0);

            double D2 = D * D;
            double D3 = D2 * D;
            double D4 = D3 * D;
            double D5 = D4 * D;
            double D6 = D5 * D;

            // Calculate latitude
            double phi = phi1 - (N1 * tanPhi1 / R1) * (
                D2 / 2
                - (5 + 3 * T1 + 10 * C1 - 4 * C1 * C1 - 9 * ep2) * D4 / 24
                + (61 + 90 * T1 + 298 * C1 + 45 * T1 * T1 - 252 * ep2 - 3 * C1 * C1) * D6 / 720
            );

            // Calculate longitude
            double lambda = lon0 + (
                D
                - (1 + 2 * T1 + C1) * D3 / 6
                + (5 - 2 * C1 + 28 * T1 - 3 * C1 * C1 + 8 * ep2 + 24 * T1 * T1) * D5 / 120
            ) / cosPhi1;

            // Convert to degrees
            double latDeg = phi * 180.0 / Math.PI;
            double lonDeg = lambda * 180.0 / Math.PI;

            return (latDeg, lonDeg);
        }

        /// <summary>
        /// Get the approximate center position for a theater
        /// </summary>
        public static (double Lat, double Lon) GetTheaterCenter(string theater)
        {
            return theater?.ToLowerInvariant() switch
            {
                "caucasus" => (42.5, 42.0),
                "syria" => (35.0, 37.0),
                "persiangulf" => (26.0, 56.0),
                "nevada" => (36.5, -115.5),
                "marianaislands" or "marianaislandswwii" => (15.0, 145.5),
                "southatlantic" or "falklands" => (-52.0, -59.0),
                "normandy" => (49.0, -1.0),
                "thechannel" => (50.5, 1.0),
                "sinai" => (30.0, 33.5),
                "kola" => (68.5, 33.0),
                "afghanistan" => (34.5, 69.0),
                _ => (42.5, 42.0)
            };
        }

    }
}
