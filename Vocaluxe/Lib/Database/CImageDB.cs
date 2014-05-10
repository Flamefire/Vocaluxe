using System;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Vocaluxe.Base;
#if WIN
using System.Data.SQLite;
using VocaluxeLib.Draw;

#else
using Mono.Data.Sqlite;
#endif

namespace Vocaluxe.Lib.Database
{
    /// <summary>
    ///     Base class for a DB that stores images
    /// </summary>
    public abstract class CImageDB : CDatabaseBase
    {
        public CImageDB(string filePath) : base(filePath) {}

        public override bool Init()
        {
            if (!base.Init())
                return false;
            if (_Version < 0 && !_CreateDB())
                return false;
            if (_Version < 1 && !_UpgradeV0V1())
                return false;
            if (_Version < 2 && !_UpgradeV1V2())
                return false;
            return true;
        }

        public bool GetImage(string fileName, ref CTexture tex)
        {
            if (_Connection == null)
                return false;

            using (var command = new SQLiteCommand(_Connection))
            {
                command.CommandText = "SELECT id, width, height FROM Images WHERE [Path] = @path";
                command.Parameters.Add("@path", DbType.String).Value = fileName;

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    if (reader != null && reader.HasRows)
                    {
                        reader.Read();
                        int id = reader.GetInt32(0);
                        int w = reader.GetInt32(1);
                        int h = reader.GetInt32(2);

                        command.CommandText = "SELECT Data FROM ImageData WHERE ImageID = @id";
                        command.Parameters.Add("@id", DbType.Int32).Value = id;
                        using (SQLiteDataReader reader2 = command.ExecuteReader())
                        {
                            if (reader2.HasRows)
                            {
                                reader2.Read();
                                byte[] data = _GetBytes(reader2);
                                tex = CDraw.AddTexture(w, h, data);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private bool _ResizeBitmap(ref Bitmap origin, int maxSize)
        {
            if (maxSize < 0)
                return true;
            int w = origin.Width;
            int h = origin.Height;
            if (w <= maxSize && h <= maxSize)
                return true;
            if (w > h)
                h = (int)Math.Round((float)maxSize / w * h);
            else
                w = (int)Math.Round((float)maxSize / h * w);
            Bitmap bmp = null;
            try
            {
                bmp = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(origin, new Rectangle(0, 0, w, h));
                //Swap images, but free origin first
                origin.Dispose();
                origin = bmp;
            }
            catch (Exception)
            {
                if (bmp != null)
                    bmp.Dispose();
                return false;
            }
            return true;
        }

        protected class CImageData
        {
            public readonly int Width;
            public readonly int Height;
            public readonly byte[] Data;

            public CImageData(int width, int height)
            {
                Width = width;
                Height = height;
                Data = new byte[width * height * 4];
            }
        }

        /// <summary>
        ///     Adds an image to the database
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="transaction">Use this transaction if specified (no commit, so do a commit on true and a rollback on false)</param>
        /// <param name="maxSize">Maximum size of the image in each dimension, -1 for full size</param>
        /// <returns>Data of the image, null on error</returns>
        protected CImageData _AddImage(string fileName, SQLiteTransaction transaction = null, int maxSize = -1)
        {
            if (!File.Exists(fileName))
                return null;
            Bitmap origin;
            try
            {
                origin = new Bitmap(fileName);
            }
            catch (Exception)
            {
                CLog.LogError("Error loading image: " + fileName);
                return null;
            }

            if (!_ResizeBitmap(ref origin, maxSize))
            {
                origin.Dispose();
                CLog.LogError("Error resizing image: " + fileName);
                return null;
            }

            CImageData imageData = new CImageData(origin.Width, origin.Height);

            try
            {
                BitmapData bmpData = origin.LockBits(new Rectangle(0, 0, origin.Width, origin.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(bmpData.Scan0, imageData.Data, 0, imageData.Data.Length);
                origin.UnlockBits(bmpData);
            }
            finally
            {
                origin.Dispose();
            }

            bool doTransaction = transaction == null;
            if (doTransaction)
                transaction = _Connection.BeginTransaction();
            try
            {
                using (var command = new SQLiteCommand("DELETE FROM Cover WHERE [Path] = @path", _Connection, transaction))
                {
                    command.Parameters.Add("@path", DbType.String, 0).Value = fileName;
                    command.ExecuteNonQuery();
                }
                using (var cmd = new SQLiteCommand("INSERT INTO Images (Path, width, height, LastAccess) VALUES (@path, @w, @h, @LastAccess)", _Connection, transaction))
                {
                    cmd.Parameters.Add("@path", DbType.String).Value = fileName;
                    cmd.Parameters.Add("@w", DbType.Int32).Value = imageData.Width;
                    cmd.Parameters.Add("@h", DbType.Int32).Value = imageData.Height;
                    cmd.Parameters.Add("@LastAccess", DbType.Int64).Value = DateTime.Now.Ticks;
                    cmd.ExecuteNonQuery();
                }
                using (var command = new SQLiteCommand("SELECT id FROM Cover WHERE [Path] = @path", _Connection, transaction))
                {
                    command.Parameters.Add("@path", DbType.String, 0).Value = fileName;
                    object idObj = command.ExecuteScalar();
                    if (idObj == null)
                        return null;
                    using (var cmd = new SQLiteCommand("INSERT INTO CoverData (CoverID, Data) VALUES (@id, @data)", _Connection, transaction))
                    {
                        cmd.Parameters.Add("@id", DbType.Int32).Value = (int)idObj;
                        cmd.Parameters.Add("@data", DbType.Binary).Value = imageData.Data;
                        cmd.ExecuteReader();
                        if (doTransaction)
                            transaction.Commit();
                        return imageData;
                    }
                }
            }
            catch (Exception e)
            {
                CLog.LogError("Error adding image " + fileName, e);
                if (doTransaction)
                    transaction.Rollback();
            }
            finally
            {
                if (doTransaction)
                    transaction.Dispose();
            }
            return null;
        }

        protected abstract bool _UpgradeV0V1();

        protected virtual bool _UpgradeV1V2()
        {
            SQLiteTransaction transaction = _Connection.BeginTransaction();
            try
            {
                using (var command = new SQLiteCommand("ALTER TABLE Images ADD LastAccess BIGINT", _Connection, transaction))
                    command.ExecuteNonQuery();
                using (var command = new SQLiteCommand("UPDATE Images SET LastAccess = @LastAccess", _Connection, transaction))
                {
                    command.Parameters.Add("@LastAccess", DbType.Int64).Value = DateTime.Now.Ticks;
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                CLog.LogError("Error on upgrade V1V2", e);
            }
            return false;
        }

        private bool _CreateDB()
        {
            try
            {
                using (SQLiteCommand command = new SQLiteCommand(_Connection))
                {
                    command.CommandText = "CREATE TABLE IF NOT EXISTS Version (Value INTEGER NOT NULL);";
                    command.ExecuteNonQuery();

                    command.CommandText = "INSERT INTO Version (Value) VALUES(0)";
                    command.ExecuteNonQuery();

                    command.CommandText = "CREATE TABLE IF NOT EXISTS Images ( id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
                                          "Path TEXT NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL);";
                    command.ExecuteNonQuery();

                    command.CommandText = "CREATE TABLE IF NOT EXISTS ImageData (ImageID INTEGER NOT NULL, Data BLOB NOT NULL);";
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                CLog.LogError("Error creating DB " + e);
                return false;
            }
            return true;
        }
    }
}