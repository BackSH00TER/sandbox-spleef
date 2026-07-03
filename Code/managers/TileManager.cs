public sealed class TileManager : Component
{
	public static TileManager Current { get; private set; }

	[Property] public GameObject TilePrefab { get; set; }
	[Property] public GameObject KillBox { get; set; }
	[Property] public int LayerCount { get; set; } = 4;

	/// <summary>Square side length of the topmost layer. Each layer below grows by 1.</summary>
	[Property] public int TopLayerSize { get; set; } = 8;

	[Property] public float LayerSpacing { get; set; } = 400f;
	[Property] public float KillBoxDropDistance { get; set; } = 500f;
	[Property] public float Padding { get; set; } = 0f;
	[Property] public bool Centered { get; set; } = true;
	[Property] public bool TintLayers { get; set; } = true;
	[Property]
	public List<Color> LayerColors { get; set; } = new()
	{

		Color.Parse( "#98E7D7" ) ?? Color.White,
		Color.Parse( "#FDEF90" ) ?? Color.White,
		Color.Parse( "#F9A6A6" ) ?? Color.White,
		Color.Parse( "#B1CBF2" ) ?? Color.White,
		Color.Parse( "#C2EBBC" ) ?? Color.White,
		Color.Parse( "#FDFBD5" ) ?? Color.White,

	};
	public List<Vector3> AvailableSpawnLocations { get; private set; } = new();

	protected override void OnEnabled()
	{
		Current = this;
	}

	protected override void OnDisabled()
	{
		if ( Current == this )
			Current = null;
	}

	public void BuildGrid()
	{
		if ( !Networking.IsHost ) return;

		if ( !TilePrefab.IsValid() )
		{
			Log.Warning( $"Platform on {GameObject.Name} has no TilePrefab assigned." );
			return;
		}

		// Spawn the first tile so we can measure its real size, then use that as the cell spacing.
		var probe = SpawnTile( Vector3.Zero, "Tile_probe", parent: null );
		var size = GetTileSize( probe );
		probe.Destroy();

		float cellX = size.x + Padding;
		float cellY = size.y + Padding;
		float cellZ = size.z + LayerSpacing;

		for ( int layer = 0; layer < LayerCount; layer++ )
		{
			int layerSize = TopLayerSize + layer;
			Vector3 layerOffset = Centered
				? new Vector3( -(layerSize - 1) * cellX * 0.5f, -(layerSize - 1) * cellY * 0.5f, 0f )
				: Vector3.Zero;

			// Each layer is parented under its own child GameObject for tidiness in the scene tree.
			// It must be NetworkSpawn'd so that when we parent network-spawned tiles under it,
			// clients can resolve the parent reference and the tiles actually appear.
			var layerTint = TintLayers ? GetLayerColor( layer ) : Color.White;
			var layerGameObject = new GameObject( true, $"Layer_{layer}" );
			layerGameObject.SetParent( GameObject );
			layerGameObject.LocalPosition = new Vector3( 0f, 0f, -layer * cellZ );
			layerGameObject.NetworkSpawn();

			for ( int x = 0; x < layerSize; x++ )
			{
				for ( int y = 0; y < layerSize; y++ )
				{
					var localPos = layerOffset + new Vector3( x * cellX, y * cellY, 0f );
					// Only the top layer contributes player spawn positions.
					if ( layer == 0 ) AvailableSpawnLocations.Add( localPos );
					var tile = SpawnTile( localPos, $"Tile_{x}_{y}", parent: layerGameObject );

					if ( TintLayers )
					{
						// Push the color through the synced LayerTint property so non-host
						// clients receive it. Setting renderer.Tint directly would only show
						// on the host since ModelRenderer.Tint isn't networked.
						var tileComponent = tile.GetComponentInChildren<Tile>();
						if ( tileComponent != null )
							tileComponent.LayerTint = layerTint;
					}
				}
			}
		}

		PositionKillBox( cellZ );
	}

	// Dynamically position the killbox under the lowest layer
	private void PositionKillBox( float cellZ )
	{
		if ( !KillBox.IsValid() ) return;

		float bottomLayerWorldZ = WorldPosition.z - (LayerCount - 1) * cellZ;
		KillBox.WorldPosition = KillBox.WorldPosition.WithZ( bottomLayerWorldZ - KillBoxDropDistance );
	}

	public void ActivateGrid()
	{
		foreach ( var tile in GameObject.GetComponentsInChildren<Tile>() )
		{
			tile.SetTriggerEnabled( true );
		}
	}

	private Color GetLayerColor( int layer )
	{
		if ( LayerColors == null || LayerColors.Count == 0 ) return Color.White;
		return LayerColors[layer % LayerColors.Count];
	}

	private GameObject SpawnTile( Vector3 localPos, string name, GameObject parent )
	{
		var tile = TilePrefab.Clone( new CloneConfig
		{
			Parent = parent ?? GameObject,
			StartEnabled = false,
			Transform = new Transform( localPos )
		} );
		tile.Name = name;

		// Always NetworkSpawn so the tile exists on every client, not just the host.
		// The probe tile spawned during measurement is host-only and destroyed immediately,
		// so it doesn't need to be networked — but networking it is harmless either way.
		tile.NetworkSpawn();

		return tile;
	}

	private Vector3 GetTileSize( GameObject tile )
	{
		BBox? bounds = null;

		foreach ( var renderer in tile.GetComponentsInChildren<ModelRenderer>() )
		{
			var b = renderer.Bounds;
			bounds = bounds.HasValue ? bounds.Value.AddBBox( b ) : b;
		}

		if ( !bounds.HasValue )
		{
			Log.Warning( $"Platform: couldn't measure tile size, falling back to 64." );
			return new Vector3( 64f, 64f, 64f );
		}

		return bounds.Value.Size;
	}




}
