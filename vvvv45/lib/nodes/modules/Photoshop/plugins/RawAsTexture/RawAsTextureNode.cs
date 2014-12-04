#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;

using SlimDX;
using SlimDX.Direct3D9;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V2.EX9;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using VVVV.Utils.SlimDX;

//here you can change the vertex type
using VertexType = VVVV.Utils.SlimDX.TexturedVertex;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "AsTexture", Category = "Raw", Help = "Creates a texture from raw bytes.", Tags = "ex9")]
	#endregion PluginInfo
	public class RawAsTextureNode : IPluginEvaluate, IPartImportsSatisfiedNotification
	{
		
		//little helper class used to store information for each
		//texture resource
		public class Info
		{
			public int Slice;
			public int Width;
			public int Height;
			public byte[] Pixels;
			public int ChannelCount;
			public int Stride;
		}
		
		#region fields & pins
		[Input("Input")]
		public ISpread<Stream> FStreamIn;
		
		[Input("Width", MinValue=0)]
		public IDiffSpread<int> FWidth;
		
		[Input("Height", MinValue=0)]
		public IDiffSpread<int> FHeight;
		
		[Input("Stride", MinValue=0)]
		public ISpread<int> FStride;
		
		[Input("Color Mode", MinValue=0, DefaultValue=1)]
		public ISpread<int> FMode;

		[Input("Channel Count", MinValue=1, DefaultValue=3)]
		public IDiffSpread<int> FChannelCount;
		
		[Input("Bits per Channel", MinValue=0, DefaultValue=8)]
		public ISpread<int> FBitsPerChannel;
		
		[Input("Apply", IsBang = true)]
		public IDiffSpread<bool> FApply;
		
		[Output("Texture Out")]
        public ISpread<TextureResource<Info>> FTextureOut;
		
		[Import]
        public ILogger FLogger;

		//when dealing with byte streams (what we call Raw in the GUI) it's always
		//good to have a byte buffer around. we'll use it when copying the data.
		readonly byte[] FBuffer = new byte[1024];
		
		#endregion fields & pins

		private void ReadBytes(Info info, int slice)
		{
			var stream=FStreamIn[slice];
			
			if (stream!=null && stream.Length!=0)
			{
				var pixels=new byte[stream.Length];
				stream.Position = 0;
				stream.Read(pixels, 0, pixels.Length);
				info.Pixels=pixels;
			}
			else
			{
				info.Pixels=null;
			}
		}
		
		//called when all inputs and outputs defined above are assigned from the host
		public void OnImportsSatisfied()
		{
			//start with an empty stream output
			FTextureOut.SliceCount = 0;
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			FTextureOut.ResizeAndDispose(spreadMax, CreateTextureResource);
			
			for (int i = 0; i < spreadMax; i++) 
			{	
				var textureResource = FTextureOut[i];
				var info = textureResource.Metadata;
		
				if (FApply[i])
				{
					textureResource.Dispose();
					textureResource = CreateTextureResource(i);
					info = textureResource.Metadata;
					
					ReadBytes(info, i);
					textureResource.NeedsUpdate = true;
				}
				else
				{
					textureResource.NeedsUpdate = false;
				}
				
				FTextureOut[i] = textureResource;
			}
		}
		
		TextureResource<Info> CreateTextureResource(int slice)
		{
			var info = new Info() { 
				Slice = slice, 
				Width = FWidth [slice], 
				Height = FHeight [slice],
				ChannelCount = FChannelCount [slice],
				Stride = FStride [slice]
			};
			
			ReadBytes(info, slice);
			return TextureResource.Create(info, CreateTexture, UpdateTexture);
		}
		
		//this method gets called, when Reinitialize() was called in evaluate,
		//or a graphics device asks for its data
		Texture CreateTexture(Info info, Device device)
		{
			if (info.Pixels != null)
			{
				return TextureUtils.CreateTexture(device, Math.Max(info.Width, 1), Math.Max(info.Height, 1));
			}
			else
			{
				return null;
			}
		
		}
		
		//this method gets called, when Update() was called in evaluate,
		//or a graphics device asks for its texture, here you fill the texture with the actual data
		//this is called for each renderer, careful here with multiscreen setups, in that case
		//calculate the pixels in evaluate and just copy the data to the device texture here
		unsafe void UpdateTexture(Info info, Texture texture)
		{			
			TextureUtils.Fill32BitTexInPlace(texture, info, FillTexure);
		}
		
		//this is a pixelshader like method, which we pass to the fill function
		unsafe void FillTexure(uint* data, int row, int col, int width, int height, Info info)
		{
			if (info.Pixels!=null)
			{
				
				var pos = row * info.Stride + Math.Min (info.Width-1, col) * info.ChannelCount;
				var src = info.Pixels;
								
				//a pixel is just a 32-bit unsigned int value
				
				uint pixel=0;
				
				switch (info.ChannelCount)
				{
					//1 channel
					case 1:
						pixel = UInt32Utils.fromARGB(0, src[pos], src[pos], src[pos]);
						break;
					
					//3 channels
					case 3:
						pixel = UInt32Utils.fromARGB(0, src[pos], src[pos+1], src[pos+2]);
						break;
					
					//4 channels
					case 4:
						pixel = UInt32Utils.fromARGB(src[pos], src[pos+1], src[pos+2], src[pos+3]);
						break;
					
					default:
						pixel = UInt32Utils.fromARGB(0, 0, 0, 0);
						break;
				}
				
				//copy pixel into texture
				TextureUtils.SetPtrVal2D(data, pixel, row, col, width);
			}
		}
	}
	
}
