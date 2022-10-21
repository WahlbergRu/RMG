# Random Map Generator
This is a Unity implementation of a map generator using a similar process to the
one outlined by [Amit Patel at Red Blob Games](http://www-cs-students.stanford.edu/~amitp/game-programming/polygon-map-generation/)

It produces maps like this:
![Screenshot 2022-10-13 122010](https://user-images.githubusercontent.com/4931005/195557749-5fe4c71e-1b77-4bb1-85c7-d5e7fa9f7f95.png)

# TODO
- rework currently virtual land, because it's not working

# Difference from SteveJohnstone
- Fixed a bug with meshsize for large models (if meshsize more than ~300-350). Change index format to int32, it possible generate mesh under 800k verts. 
- Fixed recursive code for ocean (unity broken with exception)
- Fixed falloff river bug
- Added humidity layer
- Added heat layer
- Added precipitation layer
- Added bioms layer like on image with configurable from editor ![image](https://user-images.githubusercontent.com/4931005/195560213-b39c3680-2067-4703-a734-2b7b38f0d906.png)
- Added prefabs layer to every centroid with random choosen from a list
- Added town layer instead biome town

## References
- SteveJohnstone Voronoi Map Gen project
  - https://github.com/SteveJohnstone/VoronoiMapGen
  - Licence: MIT
- jceipek's Unity-delaunay project
  - https://github.com/jceipek/Unity-delaunay
  - Licence: MIT
- Sebastian Lague's Terrain project
  - https://github.com/SebLague/Procedural-Landmass-Generation
  - Licence: MIT
