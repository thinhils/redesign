﻿namespace FluentScheduler
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A job schedule.
    /// </summary>
    public class Schedule
    {
        private readonly Action _job;

        private readonly TimeCalculator _calculator;

        private readonly object _lock;

        private Task _task;

        private CancellationTokenSource _tokenSource;

        /// <summary>
        /// Creates a new schedule for the given job.
        /// </summary>
        /// <param name="job">Job to be scheduled</param>
        /// <param name="specifier">Fluent specifier that determines when the job should run</param>
        /// <returns>A schedule for the given job</returns>
        public Schedule(Action job, Action<RunSpecifier> specifier)
        {
            _job = job;
            _calculator = new TimeCalculator();
            _lock = new object();
            _task = null;
            _tokenSource = null;

            specifier(new RunSpecifier(_calculator));
        }

        /// <summary>
        /// True if the schedule is started, false otherwise.
        /// </summary>
        public bool Running
        {
            get
            {
                lock (_lock)
                {
                    return _Running();
                }
            }
        }

        /// <summary>
        /// Date and time of the next job run.
        /// </summary>
        public DateTime? NextRun { get; private set; }

        /// <summary>
        /// Event raised when the job starts.
        /// </summary>
        public event EventHandler<JobStartedEventArgs> JobStarted;

        /// <summary>
        /// Evemt raised when the job ends.
        /// </summary>
        public event EventHandler<JobEndedEventArgs> JobEnded;

        /// <summary>
        /// Starts the schedule.
        /// </summary>
        /// <returns>
        /// True if the schedule is started, false if the scheduled was already started and the call did nothing
        /// </returns>
        public bool Start()
        {
            lock (_lock)
            {
                if (_Running())
                    return false;

                CalculateNextRun();

                _tokenSource = new CancellationTokenSource();
                _task = Run(_tokenSource.Token);

                return true;
            }
        }

        /// <summary>
        /// Stops the schedule.
        /// This calls doesn't block (it doesn't wait for the running job to end its execution).
        /// </summary>
        /// <returns>
        /// True if the schedule is stopped, false if the scheduled wasn't started and the call did nothing
        /// </returns>
        public bool Stop()
        {
            return _Stop(false, null);
        }

        /// <summary>
        /// Stops the schedule.
        /// This calls blocks (it waits for the running job to end its execution).
        /// </summary>
        /// <returns>
        /// True if the schedule is stopped, false if the scheduled wasn't started and the call did nothing
        /// </returns>
        public bool StopAndBlock()
        {
            return _Stop(false, null);
        }

        /// <summary>
        /// Stops the schedule.
        /// This calls blocks (it waits for the running job to end its execution).
        /// </summary>
        /// <param name="millisecondsTimeout">Milliseconds to wait</param>
        /// <returns>
        /// True if the schedule is stopped, false if the scheduled wasn't started and the call did nothing
        /// </returns>
        public bool StopAndBlock(int millisecondsTimeout)
        {
            return _Stop(false, millisecondsTimeout);
        }

        /// <summary>
        /// Stops the schedule.
        /// This calls blocks (it waits for the running job to end its execution).
        /// </summary>
        /// <param name="timeout">Time to wait</param>
        /// <returns>
        /// True if the schedule stopped, false if the scheduled wasn't started and the call did nothing
        /// </returns>
        public bool StopAndBlock(TimeSpan timeout)
        {
            return _Stop(false, timeout.Milliseconds);
        }

        private void CalculateNextRun()
        {
            NextRun = _calculator.Calculate(DateTime.Now);
        }

        private async Task Run(CancellationToken token)
        {
            // checking if it's supposed to run
            // it assumes that CalculateNextRun has been called previously from somewhere else
            if (!NextRun.HasValue)
                return;

            // calculating delay
            var delay = NextRun.Value - DateTime.Now;

            // delaying until it's time to run
            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, token);

            // used on both JobStarted and JobEnded events
            var startTime = DateTime.Now;

            // raising JobStarted event
            JobStarted?.Invoke(this, new JobStartedEventArgs(startTime));

            // used on JobEnded event
            Exception exception = null;

            try
            {
                // running the job
                _job();
            }
            catch (Exception e)
            {
                // catching the exception if any
                exception = e;
            }

            // used on JobEnded event
            var endTime = DateTime.Now;

            // calculating the next run
            // used on both JobEnded event and for the next run of this method
            CalculateNextRun();

            // raising JobEnded event
            JobEnded?.Invoke(this, new JobEndedEventArgs(exception, startTime, endTime, NextRun));

            // recursive call
            // note that the NextRun was already calculated in this run
            _task = Run(token);
        }

        private bool _Running()
        {
            // task and token source should be both null or both not null
            Debug.Assert(
                (_task == null && _tokenSource == null) ||
                (_task != null && _tokenSource != null)
            );

            return _task != null;
        }

        private bool _Stop(bool block, int? timeout)
        {
            lock (_lock)
            {
                if (!_Running())
                    return false;

                _tokenSource.Cancel();
                _tokenSource.Dispose();

                if (block && timeout.HasValue)
                    _task.Wait(timeout.Value);

                if (block && !timeout.HasValue)
                    _task.Wait();

                _task = null;
                _tokenSource = null;

                return true;
            }
        }
    }
}
