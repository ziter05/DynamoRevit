﻿using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.DesignScript.Runtime;
using Edge = Autodesk.DesignScript.Geometry.Edge;
using Face = Autodesk.DesignScript.Geometry.Face;
using ProtoPt = Autodesk.DesignScript.Geometry.Point;
using Solid = Autodesk.DesignScript.Geometry.Solid;
using Surface = Autodesk.DesignScript.Geometry.Surface;

namespace Revit.GeometryConversion
{
   [SupressImportIntoVM]
   public static class ProtoToRevitSolid
   {
     /// <summary>
     /// this method attmempts to construct a BRep from a closed solid.
     /// </summary>
     /// <param name="sol"></param>
     /// <param name="performHostUnitConversion"></param>
     /// <returns></returns>
       public static GeometryObject ToRevitType(Solid sol,
          bool performHostUnitConversion = true)
       {
           try
           {
               return ToRevitType(sol, performHostUnitConversion, BRepType.Solid);
           }
           catch
           {
               return null;
           }
       }

       /// <summary>
       /// this method attmempts to construct a BRep from a surface.
       /// </summary>
       /// <param name="surf"></param>
       /// <param name="performHostUnitConversion"></param>
       /// <returns></returns>
       public static GeometryObject ToRevitType(Surface surf,
          bool performHostUnitConversion = true)
       {
           try
           {
               return ToRevitType(surf, performHostUnitConversion, BRepType.OpenShell);
           }
           catch
           {
               return null;
           }
       }

       private static GeometryObject ToRevitType(Autodesk.DesignScript.Geometry.Topology topology,
       bool performHostUnitConversion,
       BRepType type)
       {
           List<Face> faces = null;

           if (performHostUnitConversion)
           {
               var newTopology = topology.InHostUnits();
               faces = newTopology.Faces.ToList();
               newTopology.Dispose();
           }
           else
           {
               faces = topology.Faces.ToList();
           }

           //a new brep builder for solids
           var brb = new BRepBuilder(type);

           Autodesk.Revit.DB.Solid converted = null;

           var edgeDict = new Dictionary<Edge, BRepBuilderGeometryId>();

           //foreach face in solid
           foreach (Face protoFace in faces)
           {
               var geom = protoFace.SurfaceGeometry();

               var ngeom = geom.ToNurbsSurface();
               bool flipped = false;

               // Check if the nurbs surface has flipped compared to the original surface
               if (geom.NormalAtParameter(.5, .5).Dot(ngeom.NormalAtParameter(.5, .5)) < 0)
               {
                   flipped = true;
               }

               // Create Revit nurbs surface
               var bbface = BRepBuilderSurfaceGeometry.CreateNURBSSurface(ngeom.DegreeU, ngeom.DegreeV,
                   ngeom.UKnots(), ngeom.VKnots(), ngeom.ControlPoints().SelectMany(x => x.Select(y => y.ToXyz())).ToList(),
                   ngeom.Weights().SelectMany(x => x).ToList(),
                   false,
                   new BoundingBoxUV());

               // Dispose geometries
               geom.Dispose();
               ngeom.Dispose();

               // Add face
               var faceId = brb.AddFace(bbface, flipped);

               // add loops and connected edges
               foreach (var loop in protoFace.Loops)
               {
                   var loopId = brb.AddLoop(faceId);

                   foreach (var coedge in loop.CoEdges)
                   {
                       var edge = coedge.Edge;
                       BRepBuilderGeometryId edgeId;
                       if (edgeDict.ContainsKey(edge))
                       {
                           edgeId = edgeDict[edge];
                       }
                       else
                       {
                           var curve = edge.CurveGeometry;
                           // Revit is already projecting edges onto the surface after checking for loop consistency
                           // and is quite forgiving even when we use the nurbs surface instead of 
                           // the original surface.
                           //
                           // But there are cases when edges ends up slightly outside one of the surfaces and Revit fails.
                           //
                           // This is something that we can be improve going forward.
                           edgeId = brb.AddEdge(BRepBuilderEdgeGeometry.Create(curve.ToRevitType()));
                           edgeDict[edge] = edgeId;
                       }
                       brb.AddCoEdge(loopId, edgeId, coedge.Reversed);
                   }

                   brb.FinishLoop(loopId);
               }

               brb.FinishFace(faceId);
           }
           edgeDict.ToList().ForEach(x => x.Key.Dispose());

           //clean up everything
           faces.ForEach(x => x.Dispose());

           var outcome = brb.Finish();
           converted = brb.GetResult();

           if (converted == null)
           {
               throw new Exception("An unexpected failure occurred when attempting to convert the solid");
           }

           return converted;
       }
   }
}
