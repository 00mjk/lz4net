﻿// ReSharper disable InconsistentNaming

namespace LZ4n
{
	public static partial class LZ4Codec
	{
		// Update chains up to ip (excluded)
		private static unsafe void LZ4HC_Insert_64(LZ4HC_Data_Structure hc4, byte* src_p)
		{
			fixed (ushort* chainTable = hc4.chainTable)
			fixed (uint* hashTable = hc4.hashTable)
			{
				byte* src_base = hc4.src_base;
				while (hc4.nextToUpdate < src_p)
				{
					byte* p = hc4.nextToUpdate;
					int delta = (int)((p) - (hashTable[((((*(uint*)(p))) * 2654435761u) >> HASHHC_ADJUST)] + src_base));
					if (delta > MAX_DISTANCE) delta = MAX_DISTANCE;
					chainTable[((int)p) & MAXD_MASK] = (ushort)delta;
					hashTable[((((*(uint*)(p))) * 2654435761u) >> HASHHC_ADJUST)] = (uint)((p) - src_base);
					hc4.nextToUpdate++;
				}
			}
		}

		private static unsafe int LZ4HC_CommonLength_64(byte* p1, byte* p2, byte* src_LASTLITERALS)
		{
			fixed (int* debruijn64 = DEBRUIJN_TABLE_64)
			{
				byte* p1t = p1;

				while (p1t < src_LASTLITERALS - (STEPSIZE_64 - 1))
				{
					var diff = (*(long*)(p2)) ^ (*(long*)(p1t));
					if (diff == 0)
					{
						p1t += STEPSIZE_64;
						p2 += STEPSIZE_64;
						continue;
					}
					p1t += debruijn64[((ulong)((ulong)((diff) & -(diff)) * 0x0218A392CDABBD3FL)) >> 58];
					return (int)(p1t - p1);
				}
				if ((p1t < (src_LASTLITERALS - 3)) && ((*(uint*)(p2)) == (*(uint*)(p1t))))
				{
					p1t += 4;
					p2 += 4;
				}
				if ((p1t < (src_LASTLITERALS - 1)) && ((*(ushort*)(p2)) == (*(ushort*)(p1t))))
				{
					p1t += 2;
					p2 += 2;
				}
				if ((p1t < src_LASTLITERALS) && (*p2 == *p1t)) p1t++;
				return (int)(p1t - p1);
			}
		}

		private static unsafe int LZ4HC_InsertAndFindBestMatch_64(
			LZ4HC_Data_Structure hc4, byte* src_p, byte* src_LASTLITERALS, ref byte* matchpos)
		{
			fixed (ushort* chainTable = hc4.chainTable)
			fixed (uint* hashTable = hc4.hashTable)
			{
				byte* xxx_ref;
				byte* src_base = hc4.src_base;
				int nbAttempts=MAX_NB_ATTEMPTS;
				int repl=0, ml=0;
				ushort delta = 0;

				// HC4 match finder
				LZ4HC_Insert_64(hc4, src_p);
				xxx_ref = (hashTable[((((*(uint*)(src_p))) * 2654435761u) >> HASHHC_ADJUST)] + src_base);

				// Detect repetitive sequences of length <= 4
				if (xxx_ref >= src_p-4) // potential repetition
				{
					if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p))) // confirmed
					{
						delta = (ushort)(src_p-xxx_ref);
						repl = ml = LZ4HC_CommonLength_64(src_p+MINMATCH, xxx_ref+MINMATCH, src_LASTLITERALS) + MINMATCH;
						matchpos = xxx_ref;
					}
					xxx_ref = ((xxx_ref) - (int)chainTable[((int)xxx_ref) & MAXD_MASK]);
				}

				while ((xxx_ref >= src_p-MAX_DISTANCE) && (nbAttempts != 0))
				{
					nbAttempts--;
					if (*(xxx_ref+ml) == *(src_p+ml))
						if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p)))
						{
							int mlt = LZ4HC_CommonLength_64(src_p+MINMATCH, xxx_ref+MINMATCH, src_LASTLITERALS) + MINMATCH;
							if (mlt > ml) { ml = mlt; matchpos = xxx_ref; }
						}
					xxx_ref = ((xxx_ref) - (int)chainTable[((int)xxx_ref) & MAXD_MASK]);
				}

				// Complete table
				if (repl != 0)
				{
					byte* ptr = src_p;
					byte* end;

					end = src_p + repl - (MINMATCH-1);
					while (ptr < end-delta)
					{
						chainTable[((int)ptr) & MAXD_MASK] = delta; // Pre-Load
						ptr++;
					}
					do
					{
						chainTable[((int)ptr) & MAXD_MASK] = delta;
						hashTable[((((*(uint*)(ptr))) * 2654435761u) >> HASHHC_ADJUST)] = (uint)((ptr) - src_base); // Head of chain
						ptr++;
					} while (ptr < end);
					hc4.nextToUpdate = end;
				}

				return (int)ml;
			}
		}

		private static unsafe int LZ4HC_InsertAndGetWiderMatch_64(
			LZ4HC_Data_Structure hc4, byte* src_p, byte* startLimit, byte* src_LASTLITERALS, int longest,
			ref byte* matchpos, ref byte* startpos)
		{
			fixed (ushort* chainTable = hc4.chainTable)
			fixed (uint* hashTable = hc4.hashTable)
			fixed (int* debruijn64 = DEBRUIJN_TABLE_64)
			{
				byte* src_base = hc4.src_base;
				byte* xxx_ref;
				int nbAttempts = MAX_NB_ATTEMPTS;
				int delta = (int)(src_p-startLimit);

				// First Match
				LZ4HC_Insert_64(hc4, src_p);
				xxx_ref = (hashTable[((((*(uint*)(src_p))) * 2654435761u) >> HASHHC_ADJUST)] + src_base);

				while ((xxx_ref >= src_p-MAX_DISTANCE) && (nbAttempts != 0))
				{
					nbAttempts--;
					if (*(startLimit + longest) == *(xxx_ref - delta + longest))
					{
						if ((*(uint*)(xxx_ref)) == (*(uint*)(src_p)))
						{
							byte* reft = xxx_ref+MINMATCH;
							byte* ipt = src_p+MINMATCH;
							byte* startt = src_p;

							while (ipt<src_LASTLITERALS-(STEPSIZE_64-1))
							{
								var diff = (*(long*)(reft)) ^ (*(long*)(ipt));
								if (diff == 0) { ipt+=STEPSIZE_64; reft+=STEPSIZE_64; continue; }
								ipt += debruijn64[((ulong)((ulong)((diff) & -(diff)) * 0x0218A392CDABBD3FL)) >> 58];
								goto _endCount;
							}
							if ((ipt<(src_LASTLITERALS-3)) && ((*(uint*)(reft)) == (*(uint*)(ipt)))) { ipt+=4; reft+=4; }
							if ((ipt<(src_LASTLITERALS-1)) && ((*(ushort*)(reft)) == (*(ushort*)(ipt)))) { ipt+=2; reft+=2; }
							if ((ipt<src_LASTLITERALS) && (*reft == *ipt)) ipt++;

						_endCount:
							reft = xxx_ref;

							while ((startt>startLimit) && (reft > hc4.src_base) && (startt[-1] == reft[-1])) { startt--; reft--; }

							if ((ipt-startt) > longest)
							{
								longest = (int)(ipt-startt);
								matchpos = reft;
								startpos = startt;
							}
						}
					}
					xxx_ref = ((xxx_ref) - (int)chainTable[((int)xxx_ref) & MAXD_MASK]);
				}

				return longest;
			}
		}

		private static unsafe int LZ4_encodeSequence_64(
			ref byte* src_p, ref byte* dst_p, ref byte* src_anchor, int matchLength, byte* xxx_ref, byte* dst_end)
		{
			byte* _p;

			int len;
			byte* xxx_token = (dst_p)++;

			// Encode Literal length
			int length = (int)(src_p - src_anchor);
			if ((dst_p + length + (2 + 1 + LASTLITERALS) + (length>>8)) > dst_end) return 1; // Check output limit
			if (length>=(int)RUN_MASK)
			{
				*xxx_token=(RUN_MASK<<ML_BITS);
				len = length-RUN_MASK;
				for (; len > 254; len-=255) *(dst_p)++ = 255;
				*(dst_p)++ = (byte)len;
			}
			else
			{
				*xxx_token = (byte)(length<<ML_BITS);
			}

			// Copy Literals
			_p = dst_p + (length);
			do
			{
				*(ulong*)dst_p = *(ulong*)src_anchor; dst_p += 8; src_anchor += 8;
			} while (dst_p < _p);
			dst_p = _p;

			// Encode Offset
			*(ushort*)dst_p = (ushort)(src_p-xxx_ref); dst_p += 2;

			// Encode MatchLength
			len = (int)(matchLength-MINMATCH);
			if (dst_p + (1 + LASTLITERALS) + (length>>8) > dst_end) return 1; // Check output limit
			if (len>=(int)ML_MASK)
			{
				*xxx_token+=ML_MASK; len-=ML_MASK;
				for (; len > 509; len-=510) { *(dst_p)++ = 255; *(dst_p)++ = 255; }
				if (len > 254) { len-=255; *(dst_p)++ = 255; }
				*(dst_p)++ = (byte)len;
			}
			else
			{
				*xxx_token += (byte)len;
			}

			// Prepare next loop
			src_p += matchLength;
			src_anchor = src_p;

			return 0;
		}

		private static unsafe int LZ4_compressHCCtx_64(
			LZ4HC_Data_Structure ctx,
			byte* src,
			byte* dst,
			int src_len,
			int dst_maxlen)
		{
			byte* src_p = (byte*)src;
			byte* src_anchor = src_p;
			byte* src_end = src_p + src_len;
			byte* src_mflimit = src_end - MFLIMIT;
			byte* src_LASTLITERALS = (src_end - LASTLITERALS);

			byte* dst_p = (byte*)dst;
			byte* dst_end = dst_p + dst_maxlen;

			int ml, ml2, ml3, ml0;
			byte* xxx_ref=null;
			byte* start2=null;
			byte* ref2=null;
			byte* start3=null;
			byte* ref3=null;
			byte* start0;
			byte* ref0;

			src_p++;

			// Main Loop
			while (src_p < src_mflimit)
			{
				ml = LZ4HC_InsertAndFindBestMatch_64(ctx, src_p, src_LASTLITERALS, ref xxx_ref);
				if (ml == 0) { src_p++; continue; }

				// saved, in case we would skip too much
				start0 = src_p;
				ref0 = xxx_ref;
				ml0 = ml;

			_Search2:
				if (src_p+ml < src_mflimit)
				{
					ml2 = LZ4HC_InsertAndGetWiderMatch_64(ctx, src_p + ml - 2, src_p + 1, src_LASTLITERALS, ml, ref ref2, ref start2);
				}
				else
				{
					ml2=ml;
				}

				if (ml2 == ml) // No better match
				{
					if (LZ4_encodeSequence_64(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0) return 0;
					continue;
				}

				if (start0 < src_p && start2 < src_p + ml0)
				{
					src_p = start0;
					xxx_ref = ref0;
					ml = ml0;
				}

				// Here, start0==ip
				if ((start2 - src_p) < 3) // First Match too small : removed
				{
					ml = ml2;
					src_p = start2;
					xxx_ref =ref2;
					goto _Search2;
				}

			_Search3:
				// Currently we have :
				// ml2 > ml1, and
				// ip1+3 <= ip2 (usually < ip1+ml1)
				if ((start2 - src_p) < OPTIMAL_ML)
				{
					int correction;
					int new_ml = ml;
					if (new_ml > OPTIMAL_ML) new_ml = OPTIMAL_ML;
					if (src_p+new_ml > start2 + ml2 - MINMATCH) new_ml = (int)(start2 - src_p) + ml2 - MINMATCH;
					correction = new_ml - (int)(start2 - src_p);
					if (correction > 0)
					{
						start2 += correction;
						ref2 += correction;
						ml2 -= correction;
					}
				}

				// Now, we have start2 = ip+new_ml, with new_ml=min(ml, OPTIMAL_ML=18)

				if (start2 + ml2 < src_mflimit)
				{
					ml3 = LZ4HC_InsertAndGetWiderMatch_64(ctx, start2 + ml2 - 3, start2, src_LASTLITERALS, ml2, ref ref3, ref start3);
				}
				else
				{
					ml3=ml2;
				}

				if (ml3 == ml2) // No better match : 2 sequences to encode
				{
					// ip & ref are known; Now for ml
					if (start2 < src_p+ml) ml = (int)(start2 - src_p);

					// Now, encode 2 sequences
					if (LZ4_encodeSequence_64(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0) return 0;
					src_p = start2;
					if (LZ4_encodeSequence_64(ref src_p, ref dst_p, ref src_anchor, ml2, ref2, dst_end) != 0) return 0;
					continue;
				}

				if (start3 < src_p+ml+3) // Not enough space for match 2 : remove it
				{
					if (start3 >= src_p + ml) // can write Seq1 immediately ==> Seq2 is removed, so Seq3 becomes Seq1
					{
						if (start2 < src_p+ml)
						{
							int correction = (int)(src_p+ml - start2);
							start2 += correction;
							ref2 += correction;
							ml2 -= correction;
							if (ml2 < MINMATCH)
							{
								start2 = start3;
								ref2 = ref3;
								ml2 = ml3;
							}
						}

						if (LZ4_encodeSequence_64(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0) return 0;
						src_p = start3;
						xxx_ref = ref3;
						ml = ml3;

						start0 = start2;
						ref0 = ref2;
						ml0 = ml2;
						goto _Search2;
					}

					start2 = start3;
					ref2 = ref3;
					ml2 = ml3;
					goto _Search3;
				}

				// OK, now we have 3 ascending matches; let's write at least the first one
				// ip & ref are known; Now for ml
				if (start2 < src_p+ml)
				{
					if (start2 - src_p < (int)ML_MASK)
					{
						int correction;
						if (ml > OPTIMAL_ML) ml = OPTIMAL_ML;
						if (src_p + ml > start2 + ml2 - MINMATCH) ml = (int)(start2 - src_p) + ml2 - MINMATCH;
						correction = ml - (int)(start2 - src_p);
						if (correction > 0)
						{
							start2 += correction;
							ref2 += correction;
							ml2 -= correction;
						}
					}
					else
					{
						ml = (int)(start2 - src_p);
					}
				}
				if (LZ4_encodeSequence_64(ref src_p, ref dst_p, ref src_anchor, ml, xxx_ref, dst_end) != 0) return 0;

				src_p = start2;
				xxx_ref = ref2;
				ml = ml2;

				start2 = start3;
				ref2 = ref3;
				ml2 = ml3;

				goto _Search3;
			}

			// Encode Last Literals
			int lastRun = (int)(src_end - src_anchor);
			if (((byte*)dst_p - dst) + lastRun + 1 + ((lastRun+255-RUN_MASK)/255) > (uint)dst_maxlen) return 0; // Check output limit
			if (lastRun>=(int)RUN_MASK)
			{
				*dst_p++=(RUN_MASK<<ML_BITS);
				lastRun-=RUN_MASK;
				for (; lastRun > 254; lastRun-=255) *dst_p++ = 255;
				*dst_p++ = (byte)lastRun;
			}
			else
			{
				*dst_p++ = (byte)(lastRun<<ML_BITS);
			}
			BlockCopy(src_anchor, dst_p, (int)(src_end - src_anchor));
			dst_p += src_end-src_anchor;

			// End
			return (int)(((byte*)dst_p)-dst);
		}
	}
}

// ReSharper restore InconsistentNaming