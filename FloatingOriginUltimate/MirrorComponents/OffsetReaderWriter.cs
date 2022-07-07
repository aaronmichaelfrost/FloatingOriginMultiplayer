#if MIRROR_43_0_OR_NEWER
using Mirror;

namespace twoloop
{
    public static class OffsetReaderWriter
    {
        public static void WriteOffset(this NetworkWriter writer, FloatingOrigin.Offset value)
        {
            switch (FloatingOrigin.singleton.precisionMode)
            {
                case FloatingOrigin.OffsetPrecisionMode.Float:
                    writer.WriteFloat(value.vector.x);
                    writer.WriteFloat(value.vector.y);
                    writer.WriteFloat(value.vector.z);
                    break;
                
                case FloatingOrigin.OffsetPrecisionMode.Double:
                    writer.WriteDouble(value.xDouble);
                    writer.WriteDouble(value.yDouble);
                    writer.WriteDouble(value.zDouble);
                    break;

                case FloatingOrigin.OffsetPrecisionMode.Decimal:
                    writer.WriteDecimal(value.xDecimal);
                    writer.WriteDecimal(value.yDecimal);
                    writer.WriteDecimal(value.zDecimal);
                    break;
            }
        }

        public static FloatingOrigin.Offset ReadOffset(this NetworkReader reader)
        {
            switch (FloatingOrigin.singleton.precisionMode)
            {
                case FloatingOrigin.OffsetPrecisionMode.Float:
                    return FloatingOrigin.Offset.CreateWithFloat(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
                
                case FloatingOrigin.OffsetPrecisionMode.Double:
                    return FloatingOrigin.Offset.CreateWithDouble(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
                
                case FloatingOrigin.OffsetPrecisionMode.Decimal:
                    return FloatingOrigin.Offset.CreateWithDecimal(reader.ReadDecimal(), reader.ReadDecimal(), reader.ReadDecimal());
            }

            return new FloatingOrigin.Offset();
        }
    }
}
#endif